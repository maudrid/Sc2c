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
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Game;

/// <summary>
/// Command line, given after "--":
///   godot --path game -- --host [--port N] [--name NAME] [--load FILE]
///   godot --path game -- --join ADDRESS[:PORT] [--name NAME]
/// Both take --content PATH: the UQM content package (or its folder), if
/// not the game's own downloaded copy.
/// Testing options: --id ID (player id instead of the saved one),
/// --script "thrust:40,right:10,none:20" (scripted input instead of keys; left, right, thrust, action, fire, takeoff
/// (Escape: in a battle, a retreat), special (a battle's special) and none are held for their frames;
/// up, down, select, cancel, pageup, pagedown, menuleft and menuright press a menu key once as their step starts (the orbit
/// menu's and its Manifest's roster's, the hyperspace and system menus' (cancel opens them in flight; stage 3c item 8), the starbase's and its shipyard's, the host's pick screen's in a battle with fleet
/// ships; menuleft and menuright are the menus' left and right), give opens or closes the giving screen,
/// pause is the host's pause key),
/// --first-run-use FILE (the first-run screen acts as if the player chose
/// that file), --quit-after FRAMES, --quit-after-join FRAMES (host), --report FILE (write what happened as JSON),
/// --save FILE, --no-upnp, --seconds-per-day N, --start-orbit STAR:PLANET[:MOON] (at a world whose orbit starts a
/// race's talk, the Ilwrath homeworld 220:0, the talk starts instead, the ship by the world),
/// --start-visit [STEP] (host: a new game starts in the commander's talk at Earth's starbase: his first, or with base-report
/// the one where the Lunar base is reported, the errands done, whose replies bring the Ilwrath and its fight),
/// --start-docked (host: a new game starts docked at Earth's starbase's menu, the opening over),
/// --start-battle RACE[,RACE...] (host: a new game starts after the opening, the host's ship in flight by Earth in a
/// battle against a fleet of ported races' ships, by data/ships.json's id: ilwrath, arilou, spathi, supox, druuge, yehat,
/// zoqfot, thradd, umgah, utwig, mycon, androsyn, mmrnmhrm, chenjesu, slylandr, urquan, blackurq (the Kohr-Ah), human
/// (the Earthling), chmmr, shofixti, syreen, orz, melnorme, vux or pkunk; up to 14 ships, each warping in when the one
/// before it dies, and a win pays the first ship's race's bounty for each; Escape retreats; with fleet ships, from
/// --start-fleet, the host chooses each ship to send in on the pick screen, the flagship the last choice),
/// --start-encounter SHIP[:SHIPS] (host: a group of that ship's race, by data/ships.json's id as --start-battle's or a
/// race by races.h's name, YEHAT_REBEL_SHIP for the Yehat rebels, of SHIPS ships or the race's fewest in a group, appears by the host and flies at it: in hyperspace with --start-at, else a
/// new game after the opening, in flight by Earth, in Sol's inner system; touching it meets it: the encounter screen, chosen
/// on it or by --encounter-choice; every race's talk (stage 3b-2 item 11), logged as it goes (in Earth's system an
/// Ur-Quan group is among Sol's own groups and gives the drone's message, as UQM's talk reads it: --start-at meets them in
/// hyperspace; a Chmmr group is emptied after its talk, with no battle; the Talking Pet has no ship or race to name: its
/// talk is its world's, stage 3c's); an attack, or a talk ending hostile, fights its ships; the Syreen and the humans,
/// who have no talk in space, are refused), --encounter-choice
/// hail|attack (host: the choice at a meeting's encounter screen, made as it comes up), --meeting-replies I[,I...] (host:
/// the replies chosen in the host's meetings' talks, each by its place among those offered from 0, one a set in turn
/// as each set comes, none while the host has paused; past the
/// last the host waits, and a place past those offered is logged and the rest dropped),
/// --start-minerals N,N,N,N,N,N,N,N (host: the hold starts with these minerals, by element category, to sell at the starbase),
/// --start-units N (host: the wallet starts with N resource units),
/// --start-credits N (host: the host starts with N Melnorme credits, to give or trade with), --start-fuel N (host: the
/// host's ship starts with N units of fuel, up to its tanks; 0 strands it, for the Melnorme's rescue),
/// --start-devices DEVICE[,DEVICE] (host: these devices are had, by data/devices.json's name or number: the portal
/// spawner and the Lunar base by everyone, every other one aboard the host's ship, as devices are held by ships since
/// stage 3c item 5),
/// --start-state NAME=VALUE[,NAME=VALUE] (host: these story fields set, by data/game-state.json's name, to reach what
/// waits on stage 3c's devices and events; repeatable; a field that isn't one, a value past what its bits hold, or a
/// field each talk, ship or player keeps of its own (StoryState.NotShared: GLOBAL_FLAGS_AND_DATA, the Melnorme's
/// credits, ...) is refused, but USED_BROADCASTER and the devices held by ships, which are set on the host's ship; set
/// before the other --start options, so --start-orbit's world sees them),
/// (a --start option refused, with a notice, stops the ones after it: --start-state, then --start-devices, then
/// --start-allied, then --start-extinct, then --start-fleet, then --start-date, then the others in the order Main.ApplyStartOptions applies them; --encounter-choice, --meeting-replies, --guests-in-orbit
/// and --guests-in-battle are set before them all, so a refused one doesn't drop those),
/// --start-fleet SHIP[:CREW][,SHIP[:CREW]] (host: the host's fleet is these ships, up to 12, by data/ships.json's id,
/// each with that crew or its race's starting crew; not androsyn, chenjesu or mmrnmhrm, races with no fleet; set
/// before --start-at and --start-orbit, so --start-orbit's world sees it and what its talk gives stays),
/// --start-allied SHIP[,SHIP] (host: these ships' races are allied, so the shipyard builds their ships, by
/// data/ships.json's id, as --start-fleet's; set before --start-fleet, --start-at and --start-orbit, so
/// --start-orbit's world sees them),
/// --start-extinct SHIP[,SHIP] (host: these ships' races are extinct, their fleets gone, by data/ships.json's id, as
/// --start-allied's, so their homeworlds' ruins can be reached; set after --start-allied and before --start-fleet,
/// --start-at and --start-orbit, so --start-orbit's world sees them),
/// --start-date YYYY-MM-DD (host: the clock moved on to that day, no earlier than the game's, each day passing as
/// the starbase's two weeks pass, its events run and the fleets moved, but no hyperspace encounters rolled; set after
/// --start-state, --start-devices, --start-allied, --start-extinct and --start-fleet, so the events see them, and before
/// the place),
/// --guests-in-orbit (host: guests joining for the first time start in
/// orbit where the host is), --guests-in-battle (host: guests joining for the first time join the battle by the host,
/// as if they'd flown into its marker), --start-at X:Y (in hyperspace, universe units), --screenshot FILE.png
/// (save the last frame on quit; needs a display, not --headless), --show-about (start with the about screen open),
/// --first-run-tick FILE[,FILE] (the first-run screen ticks these add-ons, by file name, before --first-run-use),
/// --start-site X:Y (the landing site starts at this scan map pixel, not the middle: a script can't move it),
/// --show-report RESOURCE[:ENTRY] (shows that lander report, its first entry or the one given, as if the player's lander had found it: on the screen when the game starts in orbit, with --start-orbit; else a notice).
/// --start-at, --start-orbit, --start-visit, --start-docked, --start-battle and --start-encounter never load the default save
/// nor write over it: a new game unless --load names one (then they apply to it), saved only to a --save file;
/// --start-minerals, --start-units, --start-credits, --start-fuel, --start-devices, --start-state, --start-fleet, --start-allied,
/// --start-extinct and --start-date alone change whatever game is loaded, and are saved with it.
/// With no options the game starts as a solo host.
/// </summary>
public sealed class Options
{
    public bool Join { get; private set; }
    public string Address { get; private set; } = "127.0.0.1";
    public int Port { get; private set; } = Protocol.DefaultPort;
    public string? Name { get; private set; }
    public string? PlayerId { get; private set; }
    public string? LoadPath { get; private set; }
    public string? SavePath { get; private set; }
    public string? ReportPath { get; private set; }
    public string? ScreenshotPath { get; private set; }
    public long QuitAfterFrames { get; private set; }

    /// <summary>--content PATH: the UQM content package (or its folder) to use.</summary>
    public string? ContentPath { get; private set; }

    /// <summary>--first-run-tick FILE[,FILE]: add-ons (by file name) the first-run screen ticks (testing).</summary>
    public string[] FirstRunTick { get; private set; } = Array.Empty<string>();

    /// <summary>--show-about: the about screen is open from the start (testing, screenshots).</summary>
    public bool ShowAbout { get; private set; }

    /// <summary>--first-run-use FILE: the first-run screen acts as if the player chose this file (testing).</summary>
    public string? FirstRunUse { get; private set; }

    /// <summary>--quit-after-join FRAMES: a host quits this many frames after the first guest joins (testing).</summary>
    public long QuitAfterJoinFrames { get; private set; }
    public bool UseUpnp { get; private set; } = true;
    public double? SecondsPerDay { get; private set; }
    /// <summary>--script: scripted input instead of keys (<see cref="InputScript"/>).</summary>
    public InputScript? Script { get; private set; }

    /// <summary>--start-orbit star:planet[:moon]: a new game starts in orbit there (testing and debugging).</summary>
    public (int Star, int Planet, int? Moon)? StartOrbit { get; private set; }

    /// <summary>--start-site X:Y: the landing site starts at this scan map pixel, not the middle (testing: a script can't move it; stage 3c item 5).</summary>
    public (int X, int Y)? StartSite { get; private set; }

    /// <summary>--start-visit: a new game starts at Earth's starbase, in the commander's talk (testing).</summary>
    public bool StartVisit { get; private set; }

    /// <summary>--start-visit STEP: the talk it starts in (<see cref="HostSession.VisitSteps"/>: base-report); null for his first.</summary>
    public string? StartVisitStep { get; private set; }

    /// <summary>--start-docked: a new game starts docked at Earth's starbase's menu, as after the opening (testing).</summary>
    public bool StartDocked { get; private set; }

    /// <summary>--start-battle RACE[,RACE...]: a new game starts after the opening, in a battle by Earth against a fleet of these races' ships (up to 14), one at a time (testing).</summary>
    public string? StartBattle { get; private set; }

    /// <summary>--start-encounter SHIP[:SHIPS]: a group of that ship's race appears by the host (in hyperspace with --start-at, else after the opening by Earth) and flies at it (testing, stage 3b-1 item 5).</summary>
    public string? StartEncounter { get; private set; }

    /// <summary>--encounter-choice hail|attack: the host's choice at a meeting's encounter screen, made as it comes up (testing: headless runs, which have no keys).</summary>
    public EncounterAction? EncounterChoice { get; private set; }

    /// <summary>--meeting-replies I[,I...]: the host's replies in its meetings' talks, by place among those offered, one a set in turn as each comes, none while the host has paused (testing: headless runs); past the last the host waits, and a place past those offered is logged and the rest dropped.</summary>
    public int[]? MeetingReplies { get; private set; }

    /// <summary>--start-minerals N,N,N,N,N,N,N,N: the host's hold starts with these minerals, by element category (testing: selling them).</summary>
    public int[]? StartMinerals { get; private set; }

    /// <summary>--start-units N: the host's wallet starts with N resource units (testing: giving them).</summary>
    public uint? StartUnits { get; private set; }

    /// <summary>--start-credits N: the host starts with N Melnorme credits (testing: giving them).</summary>
    public uint? StartCredits { get; private set; }

    /// <summary>--start-fuel N: the host's ship starts with N units of fuel (testing: the Melnorme's rescue).</summary>
    public uint? StartFuel { get; private set; }

    /// <summary>
    /// --start-devices DEVICE[,DEVICE]: these devices are had, by name (ROSY_SPHERE_DEVICE) or number (testing: the
    /// Manifest's Devices list): the portal spawner and the Lunar base shared, every other one aboard the host's ship
    /// (stage 3c item 5).
    /// </summary>
    public string[]? StartDevices { get; private set; }

    /// <summary>--start-state NAME=VALUE[,...], repeatable: these story fields set at the start (testing: what waits on stage 3c).</summary>
    public List<string>? StartState { get; private set; }

    /// <summary>--start-fleet SHIP[:CREW][,...]: the host's fleet is these ships (testing).</summary>
    public string[]? StartFleet { get; private set; }

    /// <summary>--start-allied SHIP[,...]: these ships' races are allied (testing: the shipyard builds their ships).</summary>
    public string[]? StartAllied { get; private set; }

    /// <summary>--start-extinct SHIP[,...]: these ships' races are extinct (testing: their homeworlds' ruins).</summary>
    public string[]? StartExtinct { get; private set; }

    /// <summary>--start-date YYYY-MM-DD: the clock moved on to that day, each day's events run (testing: the calendar's events).</summary>
    public string? StartDate { get; private set; }

    /// <summary>--guests-in-orbit: guests joining for the first time are put in orbit where the host is (testing).</summary>
    public bool GuestsInOrbit { get; private set; }

    /// <summary>--guests-in-battle: guests joining for the first time join the battle by the host, as if they'd flown into its marker (testing).</summary>
    public bool GuestsInBattle { get; private set; }

    /// <summary>--show-report RESOURCE[:ENTRY]: the lander report (a string table, text.moonbase; its first entry, or the one given: text.umgahcaster:1) shown as the finder's at start: on the screen in orbit (--start-orbit), else a notice (testing).</summary>
    public (string Resource, int Entry)? ShowReport { get; private set; }

    /// <summary>--start-at X:Y: the host starts in hyperspace at these universe coordinates (testing and debugging).</summary>
    public (short X, short Y)? StartAt { get; private set; }

    /// <summary>
    /// Whether an option puts the host somewhere (--start-at, --start-orbit,
    /// --start-visit, --start-docked, --start-battle): a testing start, which
    /// never loads the default save nor writes over it; a new game unless
    /// --load names one, saved only to a --save file.
    /// </summary>
    public bool StartsNewGame => StartAt != null || StartOrbit != null || StartVisit || StartDocked || StartBattle != null
                                 || StartEncounter != null;

    public static Options Parse(string[] args)
    {
        var o = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");
            switch (args[i])
            {
                case "--host":
                    o.Join = false;
                    break;
                case "--join":
                    o.Join = true;
                    string target = Next();
                    int colon = target.LastIndexOf(':');
                    if (colon > 0)
                    {
                        o.Address = target[..colon];
                        o.Port = int.Parse(target[(colon + 1)..]);
                    }
                    else
                        o.Address = target;
                    break;
                case "--port": o.Port = int.Parse(Next()); break;
                case "--name": o.Name = Next(); break;
                case "--id": o.PlayerId = Next(); break;
                case "--load": o.LoadPath = Next(); break;
                case "--save": o.SavePath = Next(); break;
                case "--report": o.ReportPath = Next(); break;
                case "--screenshot": o.ScreenshotPath = Next(); break;
                case "--quit-after": o.QuitAfterFrames = long.Parse(Next()); break;
                case "--content": o.ContentPath = Next(); break;
                case "--first-run-use": o.FirstRunUse = Next(); break;
                case "--show-about": o.ShowAbout = true; break;
                case "--first-run-tick": o.FirstRunTick = Next().Split(','); break;
                case "--quit-after-join": o.QuitAfterJoinFrames = long.Parse(Next()); break;
                case "--no-upnp": o.UseUpnp = false; break;
                case "--guests-in-orbit": o.GuestsInOrbit = true; break;
                case "--guests-in-battle": o.GuestsInBattle = true; break;
                case "--seconds-per-day":
                    o.SecondsPerDay = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--script": o.Script = InputScript.Parse(Next()); break;
                case "--start-visit":
                    o.StartVisit = true;
                    // An optional step: the next argument, unless it's an option.
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        o.StartVisitStep = args[++i];
                        if (!HostSession.VisitSteps.Contains(o.StartVisitStep))
                            throw new ArgumentException($"--start-visit has no step {o.StartVisitStep} (steps: {string.Join(", ", HostSession.VisitSteps)})");
                    }
                    break;
                case "--start-docked": o.StartDocked = true; break;
                case "--start-battle": o.StartBattle = Next(); break;
                case "--start-encounter": o.StartEncounter = Next(); break;
                case "--encounter-choice":
                    o.EncounterChoice = Next() switch
                    {
                        "hail" => EncounterAction.Hail,
                        "attack" => EncounterAction.Attack,
                        var c => throw new ArgumentException($"--encounter-choice is hail or attack, not {c}"),
                    };
                    break;
                case "--meeting-replies":
                    o.MeetingReplies = Array.ConvertAll(Next().Split(','), int.Parse);
                    if (Array.Exists(o.MeetingReplies, r => r < 0))
                        throw new ArgumentException("--meeting-replies are places among the replies offered, from 0");
                    break;
                case "--start-minerals":
                    o.StartMinerals = Array.ConvertAll(Next().Split(','), int.Parse);
                    break;
                case "--start-units": o.StartUnits = uint.Parse(Next()); break;
                case "--start-credits": o.StartCredits = uint.Parse(Next()); break;
                case "--start-fuel": o.StartFuel = uint.Parse(Next()); break;
                case "--start-devices": o.StartDevices = Next().Split(',', StringSplitOptions.TrimEntries); break;
                case "--start-state":
                    (o.StartState ??= new()).AddRange(Next().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
                    break;
                case "--start-fleet": o.StartFleet = Next().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break;
                case "--start-allied": o.StartAllied = Next().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break;
                case "--start-extinct": o.StartExtinct = Next().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries); break;
                case "--start-date": o.StartDate = Next(); break;
                case "--show-report": o.ShowReport = ReportArg(Next()); break;
                case "--start-orbit":
                {
                    var parts = Next().Split(':');
                    o.StartOrbit = (int.Parse(parts[0]), int.Parse(parts[1]), parts.Length > 2 ? int.Parse(parts[2]) : null);
                    break;
                }
                case "--start-site":
                {
                    var parts = Next().Split(':');
                    o.StartSite = (int.Parse(parts[0]), int.Parse(parts[1]));
                    if (o.StartSite.Value.X is < 0 or >= Crateris.Core.Systems.SurfaceMap.Width
                            || o.StartSite.Value.Y is < 0 or >= Crateris.Core.Systems.SurfaceMap.Height)
                        throw new ArgumentException($"--start-site is a scan map pixel, X from 0 to {Crateris.Core.Systems.SurfaceMap.Width - 1} "
                            + $"and Y from 0 to {Crateris.Core.Systems.SurfaceMap.Height - 1}");
                    break;
                }
                case "--start-at":
                {
                    var parts = Next().Split(':');
                    o.StartAt = (short.Parse(parts[0]), short.Parse(parts[1]));
                    break;
                }
                default: throw new ArgumentException($"Unknown option {args[i]}");
            }
        }
        if (o.StartAt != null && o.StartOrbit != null)
            throw new ArgumentException("--start-at and --start-orbit can't be used together");
        if (o.StartBattle != null && (o.StartAt != null || o.StartOrbit != null || o.StartVisit || o.StartDocked))
            throw new ArgumentException("--start-battle can't be used with --start-at, --start-orbit, --start-visit or --start-docked");
        if (o.StartEncounter != null && (o.StartBattle != null || o.StartOrbit != null || o.StartVisit || o.StartDocked))
            throw new ArgumentException("--start-encounter can't be used with --start-battle, --start-orbit, --start-visit or --start-docked");
        return o;
    }

    // --show-report's RESOURCE[:ENTRY].
    private static (string, int) ReportArg(string value)
    {
        int colon = value.LastIndexOf(':');
        if (colon < 0)
            return (value, 0);
        return int.TryParse(value[(colon + 1)..], out int entry) && entry >= 0
            ? (value[..colon], entry)
            : throw new ArgumentException($"--show-report's entry is a number from 0, not {value[(colon + 1)..]}");
    }
}
