// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using Crateris.Core.Hyperspace;
using Crateris.Core.Net;
using Crateris.Core.Session;
using Crateris.Core.Systems;

namespace Crateris.Game;

/// <summary>
/// Command line, given after "--":
///   godot --path game -- --host [--port N] [--name NAME] [--load FILE]
///   godot --path game -- --join ADDRESS[:PORT] [--name NAME]
/// Both take --content PATH: the UQM content package (or its folder), if
/// not the game's own downloaded copy.
/// Testing options: --id ID (player id instead of the saved one),
/// --script "thrust:40,right:10,none:20" (scripted input instead of keys;
/// up, down, select, cancel, pageup and pagedown press an orbit menu key once as their step starts, give opens or closes the giving screen),
/// --first-run-use FILE (the first-run screen acts as if the player chose
/// that file), --quit-after FRAMES, --quit-after-join FRAMES (host), --report FILE (write what happened as JSON),
/// --save FILE, --no-upnp, --seconds-per-day N, --start-orbit STAR:PLANET[:MOON],
/// --start-visit (host: a new game starts in the commander's talk at Earth's starbase),
/// --start-docked (host: a new game starts docked at Earth's starbase's menu, the opening over),
/// --start-minerals N,N,N,N,N,N,N,N (host: the hold starts with these minerals, by element category, to sell at the starbase),
/// --start-units N (host: the wallet starts with N resource units),
/// --start-devices DEVICE[,DEVICE] (host: the group carries these devices, by data/devices.json's name or number),
/// --guests-in-orbit (host: guests joining for the first time start in
/// orbit where the host is), --start-at X:Y (in hyperspace, universe units), --screenshot FILE.png
/// (save the last frame on quit; needs a display, not --headless), --show-about (start with the about screen open),
/// --first-run-tick FILE[,FILE] (the first-run screen ticks these add-ons, by file name, before --first-run-use),
/// --show-report RESOURCE (shows that lander report as if the player's lander had found it: on the screen when the game starts in orbit, with --start-orbit; else a notice).
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

    /// <summary>--start-visit: a new game starts at Earth's starbase, in the commander's talk (testing).</summary>
    public bool StartVisit { get; private set; }

    /// <summary>--start-docked: a new game starts docked at Earth's starbase's menu, as after the opening (testing).</summary>
    public bool StartDocked { get; private set; }

    /// <summary>--start-minerals N,N,N,N,N,N,N,N: the host's hold starts with these minerals, by element category (testing: selling them).</summary>
    public int[]? StartMinerals { get; private set; }

    /// <summary>--start-units N: the host's wallet starts with N resource units (testing: giving them).</summary>
    public uint? StartUnits { get; private set; }

    /// <summary>--start-devices DEVICE[,DEVICE]: the group carries these devices, by name (ROSY_SPHERE_DEVICE) or number (testing: the Manifest's Devices list).</summary>
    public string[]? StartDevices { get; private set; }

    /// <summary>--guests-in-orbit: guests joining for the first time are put in orbit where the host is (testing).</summary>
    public bool GuestsInOrbit { get; private set; }

    /// <summary>--show-report RESOURCE: the lander report (a string table, text.moonbase) shown as the finder's at start: on the screen in orbit (--start-orbit), else a notice (testing).</summary>
    public string? ShowReport { get; private set; }

    /// <summary>--start-at X:Y: the host starts in hyperspace at these universe coordinates (testing and debugging).</summary>
    public (short X, short Y)? StartAt { get; private set; }

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
                case "--seconds-per-day":
                    o.SecondsPerDay = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--script": o.Script = InputScript.Parse(Next()); break;
                case "--start-visit": o.StartVisit = true; break;
                case "--start-docked": o.StartDocked = true; break;
                case "--start-minerals":
                    o.StartMinerals = Array.ConvertAll(Next().Split(','), int.Parse);
                    break;
                case "--start-units": o.StartUnits = uint.Parse(Next()); break;
                case "--start-devices": o.StartDevices = Next().Split(',', StringSplitOptions.TrimEntries); break;
                case "--show-report": o.ShowReport = Next(); break;
                case "--start-orbit":
                {
                    var parts = Next().Split(':');
                    o.StartOrbit = (int.Parse(parts[0]), int.Parse(parts[1]), parts.Length > 2 ? int.Parse(parts[2]) : null);
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
        return o;
    }
}
