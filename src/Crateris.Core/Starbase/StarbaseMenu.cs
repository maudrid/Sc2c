// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/starbase.c's DoStarBase,
// shipyard.c's DoShipyard and DoModifyShips for the flagship's crew),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Net;
using Crateris.Core.Systems;

namespace Crateris.Core.Starbase;

/// <summary>Which of the starbase's screens a docked player is on.</summary>
public enum StarbaseScreen : byte
{
    /// <summary>DoStarBase's menu: Talk, Outfit, Shipyard, Leave.</summary>
    Menu,

    /// <summary>Talking to the commander (the talk is sent as a <see cref="Comm.TalkView"/>).</summary>
    Talk,

    /// <summary>Outfitting (<see cref="OutfitMenu"/>).</summary>
    Outfit,

    /// <summary>The shipyard (<see cref="ShipyardMenu"/>).</summary>
    Shipyard,
}

/// <summary>
/// The shipyard's screen, for the player's own ship (DoShipyard): its menu
/// (Crew, Save/Load, Exit), and on Crew, the ship's crew: up hires one,
/// down sends one away (DMS_ModifyCrew), select or cancel goes back. The
/// escort fleet's ships come with combat (stage 4).
/// </summary>
public sealed class ShipyardMenu
{
    /// <summary>SHIPYARD_CREW, SHIPYARD_SAVELOAD and SHIPYARD_EXIT.</summary>
    public const byte Crew = 0, SaveLoad = 1, Exit = 2;

    private readonly Func<OutfitAction, bool> _act;

    public ShipyardMenu(Func<OutfitAction, bool> act) => _act = act;

    public byte State { get; private set; } = Crew;

    /// <summary>Changing the crew (DoModifyShips' crew mode on the flagship).</summary>
    public bool CrewMode { get; private set; }

    public OutfitOutcome Key(MenuKey key)
    {
        if (CrewMode)
        {
            switch (key)
            {
                case MenuKey.Up:
                    return _act(new HireCrew()) ? OutfitOutcome.Acted : OutfitOutcome.Refused;
                case MenuKey.Down:
                    return _act(new DismissCrew()) ? OutfitOutcome.Acted : OutfitOutcome.Refused;
                case MenuKey.Select or MenuKey.Cancel:
                    CrewMode = false;
                    break;
            }
            return OutfitOutcome.None;
        }
        if (key == MenuKey.Cancel || (key == MenuKey.Select && State == Exit))
            return OutfitOutcome.Left;
        if (key == MenuKey.Select)
        {
            if (State == SaveLoad)
                return OutfitOutcome.SaveLoad;
            CrewMode = true;
            return OutfitOutcome.None;
        }
        // DoMenuChooser (PM_CREW): three entries, wrapping round.
        if (key is MenuKey.Left or MenuKey.Up)
            State = (byte)(State == Crew ? Exit : State - 1);
        else if (key is MenuKey.Right or MenuKey.Down)
            State = (byte)(State == Exit ? Crew : State + 1);
        return OutfitOutcome.None;
    }
}

/// <summary>
/// A docked player's screens at the allied starbase (stage 3a, item 8),
/// run on the host from the keys their client sends, as talk replies are:
/// DoStarBase's menu (Talk, Outfit, Shipyard, Leave; up and left, down and
/// right move round it), outfitting and the shipyard. Each docked player
/// has their own at once; save and load are the host's alone.
/// </summary>
public sealed class StarbaseMenu
{
    /// <summary>TALK_COMMANDER, OUTFIT_STARSHIP, SHIPYARD and DEPART_BASE.</summary>
    public const byte TalkCommander = 0, OutfitStarship = 1, Shipyard = 2, DepartBase = 3;

    public StarbaseScreen Screen { get; internal set; } = StarbaseScreen.Menu;

    /// <summary>The menu's cursor.</summary>
    public byte State { get; internal set; } = TalkCommander;

    public OutfitMenu? Outfit { get; internal set; }

    public ShipyardMenu? Yard { get; internal set; }

    /// <summary>Who's talking to the commander, when Talk was refused for it (decision 8); null otherwise.</summary>
    public string? Busy { get; internal set; }

    /// <summary>How many times a key was refused (MENU_SOUND_FAILURE): the client sounds each new one.</summary>
    public int Refusals { get; internal set; }

    /// <summary>The screen as sent to its player.</summary>
    public StarbaseView View() => new(Screen, State, Outfit?.State ?? 0, Outfit?.Item ?? 0, Outfit?.Installing ?? false,
        Yard?.State ?? 0, Yard?.CrewMode ?? false, Busy ?? "", Refusals);
}

/// <summary>
/// A docked player's starbase screen as their client is sent it (a
/// reliable message when it changes): the screen, the menu's cursor,
/// outfitting's cursor (<see cref="OutfitMenu.State"/> and
/// <see cref="OutfitMenu.Item"/>, UQM's numbers), the shipyard's, who's
/// talking to the commander if Talk was refused, and the refusals so far.
/// </summary>
public sealed record StarbaseView(StarbaseScreen Screen, byte State, byte OutfitState, int OutfitItem, bool Installing,
    byte YardState, bool CrewMode, string Busy, int Refusals)
{
    public byte[] Encode()
    {
        var w = new WireWriter();
        w.UInt((ulong)Screen + 1);
        w.UInt(State);
        w.UInt(OutfitState);
        w.UInt((ulong)OutfitItem);
        w.UInt(Installing ? 1UL : 0);
        w.UInt(YardState);
        w.UInt(CrewMode ? 1UL : 0);
        w.String(Busy);
        w.UInt((ulong)Refusals);
        return w.Written.ToArray();
    }

    /// <summary>A view from the host; null for none (empty: not docked). A bad one is an <see cref="InvalidDataException"/>.</summary>
    public static StarbaseView? Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            return null;
        var r = new WireReader(data);
        ulong screen = r.UInt();
        if (screen == 0 || screen - 1 > (ulong)StarbaseScreen.Shipyard)
            throw new InvalidDataException("Bad starbase screen.");
        ulong state = r.UInt(), outfitState = r.UInt(), outfitItem = r.UInt(), installing = r.UInt();
        ulong yardState = r.UInt(), crewMode = r.UInt();
        string busy = r.String() ?? "";
        ulong refusals = r.UInt();
        if (state > StarbaseMenu.DepartBase || outfitState > World.ShipModules.EmptyLander || outfitItem > 15
                || installing > 1 || yardState > ShipyardMenu.Exit || crewMode > 1 || refusals > int.MaxValue)
            throw new InvalidDataException("Bad starbase screen state.");
        if (!r.AtEnd)
            throw new InvalidDataException("A starbase screen has bytes after its end.");
        return new StarbaseView((StarbaseScreen)(int)(screen - 1), (byte)state, (byte)outfitState, (int)outfitItem,
            installing == 1, (byte)yardState, crewMode == 1, busy, (int)refusals);
    }
}
