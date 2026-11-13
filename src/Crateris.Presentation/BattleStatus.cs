// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/status.c: CaptainsWindow,
// DrawBattleCrewAmount, DrawCaptainsWindow, PreProcessStatus and
// PostProcessStatus; src/uqm/shipstat.c: DrawCrewFuelString,
// DrawShipNameString, ClearShipStatus, OutlineShipStatus, InitShipStatus
// and DeltaStatistics), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Combat;
using Crateris.Core.Systems;

namespace Crateris.Presentation;

/// <summary>
/// A ship as its status panel shows it: its status icons and captain's
/// window (sheets; null for none), its most crew and energy, whether its
/// crew are robots (CREW_IMMUNE: grey units) and the captain is aboard
/// (PLAYER_CAPTAIN: the flagship's own unit, its crew as a number), and its
/// names: a ship's race name and captain's, or for the flagship (UQM's
/// SIS: <see cref="Sis"/>) its ship's name and its commander's.
/// </summary>
public sealed record StatusShip(string? Icons, string? Captain, int MaxCrew, int MaxEnergy, bool CrewImmune, bool PlayerCaptain,
    bool Sis, string Name, string CaptainName);

/// <summary>
/// A ship's state as its panel follows it each frame: its crew and energy
/// (SHIP_INFO's), its keys and whether it's low on energy
/// (cur_status_flags: <see cref="StatusFlags"/>), and its element's crew
/// and life (a ship whose crew ran out explodes, its captain's window
/// flashing as its life runs down); and whether its element is gone (its
/// explosion over: the panel stands as it last was).
/// </summary>
public readonly record struct StatusState(int Crew, int Energy, StatusFlags Flags, int ElementCrew, int LifeSpan, bool Gone = false);

/// <summary>
/// UQM's battle status panel, as status.c and shipstat.c draw it in the
/// status area (<see cref="UqmCanvas.Status"/>): two slots, the enemy's on
/// top and the player's below, each drawn whole as its ship arrives
/// (<see cref="Init"/>) and then only what changes, frame by frame
/// (<see cref="Update"/>): the crew and energy gauges a unit at a time, the
/// crew's number, the captain's window turning, thrusting and firing with
/// the ship's keys, the energy's label lit when low, and the window's
/// flash as the ship explodes. UQM's PC menus (optWhichMenu OPT_PC).
/// </summary>
public sealed class BattleStatus
{
    public const int StatusWidth = 64, StatusHeight = UqmCanvas.StatusHeight;
    public const int CrewXOffs = 4, EnergyXOffs = 52, UnitWidth = 2, UnitHeight = 1;
    public const int StatWidth = 1 + UnitWidth + 1 + UnitWidth + 1;
    public const int ShipInfoHeight = 65, GaugeYOffs = ShipInfoHeight - 10;
    public const int CaptainXOffs = 4, CaptainYOffs = ShipInfoHeight + 4, CaptainWidth = 55, CaptainHeight = 30;
    public const int ShipStatusHeight = StatusHeight >> 1;
    /// <summary>BAD_GUY_YOFFS and GOOD_GUY_YOFFS: the enemy's slot on top, the player's below.</summary>
    public const int EnemySlot = 0, PlayerSlot = ShipStatusHeight;
    public const int StarconTextHeight = 7, BattleCrewX = 10, BattleCrewY = 64, MaxCrewSize = 42;
    public const int NumExplosionFrames = 12;
    public const string StatusSheet = "graphics.status";

    private static Rgb Rgb15(int r, int g, int b) => new((byte)((r << 3) | (r >> 2)), (byte)((g << 3) | (g >> 2)), (byte)((b << 3) | (b >> 2)));

    private static readonly Rgb Black = new(0, 0, 0);
    private static readonly Rgb Panel = Rgb15(0x0A, 0x0A, 0x0A), Shade = Rgb15(0x08, 0x08, 0x08), Light = Rgb15(0x10, 0x10, 0x10);
    private static readonly Rgb CrewUnit = Rgb15(0x00, 0x14, 0x00), RobotUnit = Rgb15(0x0A, 0x0A, 0x0A),
        PlayerUnit = Rgb15(0x0A, 0x0A, 0x1F), FuelUnit = Rgb15(0x14, 0x00, 0x00), LowFuel = Rgb15(0x1F, 0x1F, 0x0A);

    private static readonly Rgb[] FlashTab0 =
    {
        Rgb15(0x0F, 0x00, 0x00), Rgb15(0x1F, 0x19, 0x19), Rgb15(0x1F, 0x11, 0x00), Rgb15(0x1F, 0x1C, 0x00), Rgb15(0x1F, 0x1F, 0x1F),
    };

    private static readonly Rgb[] FlashTab1 =
    {
        Rgb15(0x1E, 0x1F, 0x12), Rgb15(0x1F, 0x1F, 0x0A), Rgb15(0x1F, 0x1F, 0x00), Rgb15(0x1F, 0x1C, 0x00), Rgb15(0x1F, 0x18, 0x00),
        Rgb15(0x1F, 0x15, 0x00), Rgb15(0x1F, 0x11, 0x00), Rgb15(0x1F, 0x0E, 0x00), Rgb15(0x1F, 0x0A, 0x00), Rgb15(0x1F, 0x07, 0x00),
        Rgb15(0x1F, 0x03, 0x00), Rgb15(0x1B, 0x00, 0x00), Rgb15(0x17, 0x00, 0x00), Rgb15(0x13, 0x00, 0x00), Rgb15(0x0F, 0x00, 0x00),
    };

    private static readonly Rgb[] FlashTab2 = { Rgb15(0x17, 0x00, 0x00), Rgb15(0x0F, 0x00, 0x00), Rgb15(0x0B, 0x00, 0x00) };

    private const StatusFlags Keys = StatusFlags.Left | StatusFlags.Right | StatusFlags.Thrust | StatusFlags.Weapon | StatusFlags.Special;

    private readonly UqmCanvas _c;

    // Each slot's ship and what its panel shows now (SHIP_INFO's crew and
    // energy levels as drawn, old_status_flags).
    private sealed class Slot
    {
        public required StatusShip Ship;
        public int Crew, Energy;
        public StatusFlags OldFlags;
        public int ElementCrew = -1;
        public bool Flashing;
        public int LastLife;
        // The least life of its explosion whose flash step is drawn (none yet: NumExplosionFrames * 3): the steps
        // of frames missed are drawn as the next is seen, or as the slot stops.
        public int FlashedTo = NumExplosionFrames * 3;
        public bool Done;
    }

    private readonly Dictionary<int, Slot> _slots = new();

    public BattleStatus(UqmCanvas canvas) => _c = canvas;

    /// <summary>The ship in a slot (<see cref="PlayerSlot"/>, <see cref="EnemySlot"/>), or null.</summary>
    public StatusShip? ShipIn(int slot) => _slots.TryGetValue(slot, out var s) ? s.Ship : null;

    /// <summary>A ship arrives in a slot: InitShipStatus, then DrawCaptainsWindow (spawn_ship's), with its state now.</summary>
    public void Init(int y, StatusShip ship, StatusState state)
    {
        var slot = _slots[y] = new Slot { Ship = ship, OldFlags = state.Flags };
        var old = _c.SetContext(_c.Status);
        InitShipStatus(slot, y, state);
        DrawCaptainsWindow(slot, y);
        _c.SetContext(old);
    }

    /// <summary>
    /// A frame on: the captain's window's first pass for the keys changed
    /// (PreProcessStatus), the gauges to the new crew and energy
    /// (DeltaCrew's and DeltaEnergy's DeltaStatistics), then the explosion's
    /// flash, the low energy label and the window's second pass
    /// (PostProcessStatus).
    /// </summary>
    public void Update(int y, StatusState state)
    {
        if (!_slots.TryGetValue(y, out var slot) || slot.Done)
            return;
        // UQM's status runs in the ship's own steps, which end with its
        // explosion: the element gone, or past its explosion (its life set
        // going again, as cleanup_dead_ship waits for the battle's end).
        // Out of crew and its life past any explosion's (cleanup_dead_ship's wait): the explosion's over, however
        // many of its frames were missed (a snapshot lost).
        bool over = state.Crew == 0 && state.ElementCrew == 0 && state.LifeSpan > NumExplosionFrames * 3;
        if (state.Gone || over || slot.Flashing && state.LifeSpan > slot.LastLife)
        {
            // Destroyed (a retreat keeps its crew): the flash steps not drawn yet, its explosion's frames missed
            // (all of them if none was seen), down to its life's end.
            if (state.Crew == 0)
            {
                var old0 = _c.SetContext(_c.Status);
                FlashTo(slot, y, 0);
                _c.SetContext(old0);
            }
            slot.Done = true;
            return;
        }
        slot.LastLife = state.LifeSpan;
        var old = _c.SetContext(_c.Status);
        // The first frame after the ship's crew ran out, its keys are as they
        // were: UQM clears them in that frame's PostProcessStatus, after
        // reading them, so the window turns back only on the next.
        if (slot.ElementCrew == 0 && !slot.Flashing)
            state = state with { Flags = (state.Flags & ~Keys) | (slot.OldFlags & Keys) };
        var keysChanged = (slot.OldFlags ^ state.Flags) & Keys;
        if (keysChanged != 0)
            CaptainsWindow(slot, y, keysChanged, state.Flags, 1);
        DeltaStatistics(slot, y, state.Crew - slot.Crew, state.Energy - slot.Energy);
        PostProcessStatus(slot, y, state);
        _c.SetContext(old);
    }

    private void Rect(int x, int y, int w, int h) => _c.FillRect(new URect(x, y, w, h));

    // ---- shipstat.c ----

    private void DrawCrewFuelString(int y, int state)
    {
        int sy = y + GaugeYOffs + StarconTextHeight;
        if (state == 0)
            _c.Stamp(StatusSheet, 4, CrewXOffs + (StatWidth >> 1) + 6, sy);
        int ex = EnergyXOffs + (StatWidth >> 1) - 5;
        if (state >= 0)
            _c.Stamp(StatusSheet, 5, ex, sy);
        else
        {
            _c.SetForeground(LowFuel);
            _c.FilledStamp(StatusSheet, 5, ex, sy);
        }
    }

    private void DrawShipNameString(string name, int y)
    {
        var oldFont = _c.SetFont("starcon");
        int by = StarconTextHeight + 3 + y;
        _c.SetForeground(Light);
        _c.Text(StatusWidth >> 1, by, TextAlign.Center, name);
        _c.SetForeground(Black);
        _c.Text(StatusWidth >> 1, by - 1, TextAlign.Center, name);
        _c.SetFont(oldFont);
    }

    private void ClearShipStatus(int y)
    {
        _c.SetForeground(Panel);
        Rect(2, 3 + y, StatusWidth - 4, ShipInfoHeight - 3);
    }

    private void OutlineShipStatus(int y)
    {
        _c.SetForeground(Shade);
        Rect(0, 1 + y, StatusWidth, 1);
        Rect(0, 2 + y, StatusWidth - 1, 1);
        Rect(0, 2 + y, 1, ShipInfoHeight - 2);
        Rect(1, 2 + y, 1, ShipInfoHeight - 2);
        _c.SetForeground(Light);
        Rect(StatusWidth - 1, 2 + y, 1, ShipInfoHeight - 2);
        Rect(StatusWidth - 2, 3 + y, 1, ShipInfoHeight - 3);
        _c.SetForeground(Black);
        Rect(0, y, StatusWidth, 1);
    }

    private void InitShipStatus(Slot slot, int y, StatusState state)
    {
        var ship = slot.Ship;
        OutlineShipStatus(y);
        ClearShipStatus(y);
        if (ship.Icons is { } icons)
            _c.Stamp(icons, 1, StatusWidth >> 1, 31 + y);   // IncFrameIndex (icons)
        int crewHeight = ((Math.Min(ship.MaxCrew, MaxCrewSize) + 1) & ~1) + 1;
        int energyHeight = (((ship.MaxEnergy + 1) >> 1) << 1) + 1;
        _c.SetForeground(Shade);
        Rect(CrewXOffs - 1, GaugeYOffs + 1 + y, StatWidth + 2, 1);
        Rect(EnergyXOffs - 1, GaugeYOffs + 1 + y, StatWidth + 2, 1);
        Rect(EnergyXOffs + StatWidth, GaugeYOffs + 1 + y - energyHeight, 1, energyHeight);
        Rect(CrewXOffs + StatWidth, GaugeYOffs + 1 + y - crewHeight, 1, crewHeight);
        _c.SetForeground(Light);
        Rect(CrewXOffs - 1, GaugeYOffs - crewHeight + y, StatWidth + 2, 1);
        Rect(EnergyXOffs - 1, GaugeYOffs - energyHeight + y, StatWidth + 2, 1);
        Rect(EnergyXOffs - 1, GaugeYOffs - energyHeight + y, 1, energyHeight + 1);
        Rect(CrewXOffs - 1, GaugeYOffs - crewHeight + y, 1, crewHeight + 1);
        _c.SetForeground(Black);
        Rect(CrewXOffs, y - crewHeight + GaugeYOffs + 1, StatWidth, crewHeight);
        Rect(EnergyXOffs, y - energyHeight + GaugeYOffs + 1, StatWidth, energyHeight);

        if (!ship.Sis)
        {   // Any regular ship: its race's name and its captain's.
            DrawCrewFuelString(y, 0);
            DrawShipNameString(ship.Name, y);
            var oldFont = _c.SetFont("tiny");
            _c.SetForeground(Black);
            _c.Text(StatusWidth >> 1, y + GaugeYOffs + 3, TextAlign.Center, ship.CaptainName);
            _c.SetFont(oldFont);
        }
        else
        {   // The flagship: its own name.
            DrawCrewFuelString(y, 0);
            DrawShipNameString(ship.Name, y);
        }
        slot.Crew = slot.Energy = 0;
        DeltaStatistics(slot, y, state.Crew, state.Energy);
    }

    private void DeltaStatistics(Slot slot, int yOffs, int crewDelta, int energyDelta)
    {
        if (crewDelta == 0 && energyDelta == 0)
            return;
        var ship = slot.Ship;
        int y = GaugeYOffs + yOffs;
        int rx, ry;
        if (crewDelta != 0)
        {
            int oldBlocks = Math.Min(slot.Crew, MaxCrewSize);
            int newCrew = slot.Crew + crewDelta;
            int newBlocks = Math.Min(newCrew, MaxCrewSize);
            if (crewDelta > 0)
            {
                ry = (y + 1) - (((oldBlocks + 1) >> 1) * (UnitHeight + 1));
                _c.SetForeground(ship.CrewImmune ? RobotUnit : CrewUnit);
                for (int b = oldBlocks; b < newBlocks; ++b)
                {
                    rx = CrewXOffs + 1;
                    if ((b & 1) == 0)
                    {
                        rx += UnitWidth + 1;
                        ry -= UnitHeight + 1;
                    }
                    Rect(rx, ry, UnitWidth, UnitHeight);
                }
            }
            else
            {
                _c.SetForeground(Black);
                ry = (y + 1) - (((oldBlocks + 2) >> 1) * (UnitHeight + 1));
                for (int b = oldBlocks; b > newBlocks; --b)
                {
                    rx = CrewXOffs + 1 + UnitWidth + 1;
                    if ((b & 1) == 0)
                    {
                        rx -= UnitWidth + 1;
                        ry += UnitHeight + 1;
                    }
                    Rect(rx, ry, UnitWidth, UnitHeight);
                }
            }
            if (ship.PlayerCaptain && ((slot.Crew > MaxCrewSize) != (newCrew > MaxCrewSize) || slot.Crew == 0) && newCrew != 0)
            {
                _c.SetForeground(newCrew > MaxCrewSize ? CrewUnit : PlayerUnit);
                Rect(CrewXOffs + 1 + UnitWidth + 1, y - UnitHeight, UnitWidth, UnitHeight);
            }
            slot.Crew = newCrew;
            if (ship.MaxCrew > MaxCrewSize || ship.PlayerCaptain)
                DrawBattleCrewAmount(slot, yOffs);
        }
        if (energyDelta != 0)
        {
            if (energyDelta > 0)
            {
                _c.SetForeground(FuelUnit);
                ry = (y + 1) - (((slot.Energy + 1) >> 1) * (UnitHeight + 1));
                do
                {
                    rx = EnergyXOffs + 1;
                    if ((slot.Energy & 1) == 0)
                    {
                        rx += UnitWidth + 1;
                        ry -= UnitHeight + 1;
                    }
                    Rect(rx, ry, UnitWidth, UnitHeight);
                    ++slot.Energy;
                } while (--energyDelta != 0);
            }
            else
            {
                _c.SetForeground(Black);
                ry = (y + 1) - (((slot.Energy + 2) >> 1) * (UnitHeight + 1));
                do
                {
                    rx = EnergyXOffs + 1 + UnitWidth + 1;
                    if ((slot.Energy & 1) == 0)
                    {
                        rx -= UnitWidth + 1;
                        ry += UnitHeight + 1;
                    }
                    Rect(rx, ry, UnitWidth, UnitHeight);
                    --slot.Energy;
                } while (++energyDelta != 0);
            }
        }
    }

    // ---- status.c ----

    // CaptainsWindow: the captain's window as its keys change, a pass
    // before the frame (1) and after (2).
    private void CaptainsWindow(Slot slot, int y, StatusFlags delta, StatusFlags cur, int pass)
    {
        if (slot.Ship.Captain is not { } captain)
            return;
        int x = CaptainXOffs, sy = y + CaptainYOffs;
        int count = _c.FrameCount(captain);
        int Rel(int f, int n) => ((f + n) % count + count) % count;
        const int turn = 1, thrust = 6, weapon = 9, special = 12;   // DrawCaptainsWindow's frames, from the background (0)
        if ((delta & StatusFlags.Left) != 0)
        {
            int f = turn;
            if ((delta & StatusFlags.Right) == 0)
            {
                f = Rel(f, 3);
                if (pass == 2)
                    f = (cur & StatusFlags.Left) != 0 ? Rel(f, 1) : Rel(f, -1);
            }
            else if ((cur & StatusFlags.Right) != 0)
            {
                f = pass == 1 ? Rel(f, 3) : Rel(f, 1);
                _c.Stamp(captain, f, x, sy);
                f = Rel(f, -1);
            }
            else
            {
                f = pass == 1 ? Rel(f, 1) : Rel(f, 3);
                _c.Stamp(captain, f, x, sy);
                f = Rel(f, 1);
            }
            _c.Stamp(captain, f, x, sy);
        }
        else if ((delta & StatusFlags.Right) != 0)
        {
            int f = Rel(turn, 1);
            if (pass == 2)
                f = (cur & StatusFlags.Right) != 0 ? Rel(f, -1) : Rel(f, 1);
            _c.Stamp(captain, f, x, sy);
        }
        foreach (var (flag, first) in new[] { (StatusFlags.Thrust, thrust), (StatusFlags.Weapon, weapon), (StatusFlags.Special, special) })
        {
            if ((delta & flag) == 0)
                continue;
            int f = first;
            if (pass == 1)
                f = Rel(f, 1);
            else if ((cur & flag) != 0)
                f = Rel(f, 2);
            _c.Stamp(captain, f, x, sy);
        }
    }

    private void DrawBattleCrewAmount(Slot slot, int yOffs)
    {
        int bx = BattleCrewX + 2 - 8, by = BattleCrewY + yOffs;   // OPT_PC's: 8 left
        _c.SetFont("starcon");
        _c.SetForeground(Panel);
        Rect(bx, by - 5, 6 * 3 + 6, 5);
        _c.SetForeground(Black);
        _c.Text(bx, by, TextAlign.Left, slot.Crew.ToString());
    }

    private void DrawCaptainsWindow(Slot slot, int yOffs)
    {
        var ship = slot.Ship;
        _c.SetForeground(Panel);
        Rect(CaptainXOffs - 2, yOffs + ShipInfoHeight, StatusWidth - CaptainXOffs, ShipStatusHeight - CaptainYOffs + 2);
        _c.SetForeground(Shade);
        Rect(1, yOffs + ShipInfoHeight, 1, ShipStatusHeight - ShipInfoHeight - 2);
        Rect(0, yOffs + ShipInfoHeight, 1, ShipStatusHeight - ShipInfoHeight - 1);
        _c.SetForeground(Light);
        Rect(StatusWidth - 1, yOffs + ShipInfoHeight, 1, ShipStatusHeight - ShipInfoHeight);
        Rect(StatusWidth - 2, yOffs + ShipInfoHeight, 1, ShipStatusHeight - ShipInfoHeight);
        Rect(1, yOffs + ShipStatusHeight - 2, StatusWidth - 2, 1);
        Rect(0, yOffs + ShipStatusHeight - 1, StatusWidth - 1, 1);
        int y = yOffs + CaptainYOffs;
        _c.SetForeground(Shade);
        Rect(59, y, 1, 30);
        Rect(3, y + 30, 57, 1);
        _c.SetForeground(Light);
        Rect(3, y - 1, 57, 1);
        Rect(3, y, 1, 30);
        if (ship.Captain is { } captain)
            _c.Stamp(captain, 0, CaptainXOffs, y);
        if (ship.Sis)
        {   // The flagship: its commander's name over the window.
            _c.SetForeground(CrewUnit);
            _c.SetFont("tiny");
            _c.Text(StatusWidth >> 1, y + 6, TextAlign.Center, ship.CaptainName);
        }
        if (ship.MaxCrew > MaxCrewSize || ship.PlayerCaptain)
            DrawBattleCrewAmount(slot, yOffs);
    }

    private void PostProcessStatus(Slot slot, int y, StatusState state)
    {
        var cur = state.Flags;
        // The ship's crew ran out: from the frame after it's seen out (UQM's
        // crew runs out in the frame's collisions, after the ship's own
        // PostProcessStatus), its window flashes (the keys it has then are
        // the engine's, cleared as UQM's are).
        bool outBefore = slot.ElementCrew == 0;
        slot.ElementCrew = state.ElementCrew;
        // The frame its crew runs out has life 0 (do_damage), its explosion then 35 down to 0: a ship out of crew
        // first seen at a life of its explosion's has had its crew run out in a frame missed.
        bool exploding = state.ElementCrew == 0 && state.Crew == 0 && state.LifeSpan < NumExplosionFrames * 3;
        bool flash = state.ElementCrew == 0 && (outBefore || exploding && state.LifeSpan > 0);
        // Each step draws on the last: frames missed (a snapshot lost, or two in one of the screen's frames) have
        // their steps drawn now, down to this one's; the keys' changes taken as made in the first frame missed,
        // so drawn before them (UQM drew them after the first of those steps, which fills the window as the next
        // does: the same pixels).
        bool steps = flash && exploding;
        bool catchUp = steps && slot.FlashedTo - 1 > state.LifeSpan;
        if (catchUp)
            KeysChanged(slot, y, cur);
        if (flash)
            slot.Flashing = true;
        if (steps)
            FlashTo(slot, y, state.LifeSpan);
        if (!catchUp)
            KeysChanged(slot, y, cur);
    }

    // The explosion's flash steps not drawn yet, from the one after the last drawn down to life's.
    private void FlashTo(Slot slot, int y, int life)
    {
        for (int l = slot.FlashedTo - 1; l >= life; --l)
            ExplosionFlash(y, l);
        slot.FlashedTo = Math.Min(slot.FlashedTo, life);
    }

    // PostProcessStatus's keys and low energy, as they changed.
    private void KeysChanged(Slot slot, int y, StatusFlags cur)
    {
        var changed = (slot.OldFlags ^ cur) & (Keys | StatusFlags.LowOnEnergy);
        if (changed != 0)
        {
            if ((changed & StatusFlags.LowOnEnergy) != 0)
                DrawCrewFuelString(y, (cur & StatusFlags.LowOnEnergy) == 0 ? 1 : -1);
            changed &= Keys;
            if (changed != 0)
                CaptainsWindow(slot, y, changed, cur, 2);
        }
        slot.OldFlags = cur;
    }

    // The captain's window flashing as the ship explodes, by its life left.
    private void ExplosionFlash(int y, int lifeSpan)
    {
        int i = (byte)(NumExplosionFrames * 3 - 1 - lifeSpan);
        Rgb c;
        int rx, ry, rw, rh;
        if (i <= 4)
        {
            c = FlashTab0[i];
            (rx, ry, rw, rh) = (CaptainXOffs, y + CaptainYOffs, CaptainWidth, CaptainHeight);
        }
        else
        {
            _c.SetForeground(Black);
            i -= 5;
            if (i <= 14)
            {
                c = FlashTab1[i];
                (rx, ry, rw, rh) = (CaptainXOffs + i, y + CaptainYOffs + i, CaptainWidth - (i << 1), CaptainHeight - (i << 1));
                if (rh == 2)
                    ++rh;
                _c.Outline(new URect(rx, ry, rw, rh));
                ++rx;
                ++ry;
                rw -= 2;
                rh -= 2;
            }
            else if ((i -= 15) <= 4)
            {
                ry = y + CaptainYOffs + 15;
                rw = i + 1;
                rh = 1;
                int gap;
                switch (i)
                {
                    case 0:
                        rx = CaptainXOffs + 15;
                        gap = CaptainWidth - ((15 + 1) << 1);
                        c = Rgb15(0x13, 0x00, 0x00);
                        break;
                    case 1:
                        rx = CaptainXOffs + 16;
                        gap = CaptainWidth - ((17 + 1) << 1);
                        c = Rgb15(0x07, 0x00, 0x00);
                        break;
                    case 2:
                        rx = CaptainXOffs + 18;
                        gap = CaptainWidth - ((20 + 1) << 1);
                        c = Rgb15(0x1B, 0x00, 0x00);
                        break;
                    case 3:
                        rx = CaptainXOffs + 21;
                        gap = CaptainWidth - ((24 + 1) << 1);
                        c = Rgb15(0x1F, 0x00, 0x00);
                        break;
                    default:
                        rx = CaptainXOffs + 25;
                        gap = 1;
                        rw = 2;
                        // UQM's MAKE_RGB15 (0x1F, 0x50, 0x05): green's 0x50 is past 5 bits,
                        // and its 5-to-8 conversion ((g << 3) | (g >> 2)) keeps the low byte.
                        c = new Rgb(255, unchecked((byte)((0x50 << 3) | (0x50 >> 2))), (byte)((0x05 << 3) | (0x05 >> 2)));
                        break;
                }
                Rect(rx, ry, rw, rh);
                rx += gap + rw;
                Rect(rx, ry, rw, rh);
                rx -= gap;
                rw = gap;
            }
            else
            {
                c = (i -= 5) > 2 ? Black : FlashTab2[i];
                (rx, ry, rw, rh) = (CaptainXOffs + (CaptainWidth >> 1), y + CaptainYOffs + ((CaptainHeight + 1) >> 1), 1, 1);
            }
        }
        _c.SetForeground(c);
        Rect(rx, ry, rw, rh);
    }
}
