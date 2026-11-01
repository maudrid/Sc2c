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
/// up, down, select and cancel press an orbit menu key once as their step starts),
/// --quit-after FRAMES, --quit-after-join FRAMES (host), --report FILE (write what happened as JSON),
/// --save FILE, --no-upnp, --seconds-per-day N, --screenshot FILE.png
/// (save the last frame on quit; needs a display, not --headless).
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

    /// <summary>--first-run-use FILE: the first-run screen acts as if the player chose this file (testing).</summary>
    public string? FirstRunUse { get; private set; }

    /// <summary>--quit-after-join FRAMES: a host quits this many frames after the first guest joins (testing).</summary>
    public long QuitAfterJoinFrames { get; private set; }
    public bool UseUpnp { get; private set; } = true;
    public double? SecondsPerDay { get; private set; }
    /// <summary>One step of --script: input held for some frames; a menu key is pressed once as the step starts.</summary>
    public sealed record ScriptStep(int Frames, ShipInput Input, MenuKey? Key = null);

    public List<ScriptStep>? Script { get; private set; }

    /// <summary>--start-orbit star:planet[:moon]: a new game starts in orbit there (testing and debugging).</summary>
    public (int Star, int Planet, int? Moon)? StartOrbit { get; private set; }

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
                case "--quit-after-join": o.QuitAfterJoinFrames = long.Parse(Next()); break;
                case "--no-upnp": o.UseUpnp = false; break;
                case "--seconds-per-day":
                    o.SecondsPerDay = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture);
                    break;
                case "--script": o.Script = ParseScript(Next()); break;
                case "--start-orbit":
                {
                    var parts = Next().Split(':');
                    o.StartOrbit = (int.Parse(parts[0]), int.Parse(parts[1]), parts.Length > 2 ? int.Parse(parts[2]) : null);
                    break;
                }
                default: throw new ArgumentException($"Unknown option {args[i]}");
            }
        }
        return o;
    }

    private static List<ScriptStep> ParseScript(string text)
    {
        var steps = new List<ScriptStep>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            var input = ShipInput.None;
            MenuKey? menuKey = null;
            foreach (var key in kv[0].Split('+'))
            {
                MenuKey? mk = key.Trim() switch
                {
                    "up" => MenuKey.Up,
                    "down" => MenuKey.Down,
                    "select" => MenuKey.Select,
                    "cancel" => MenuKey.Cancel,
                    _ => null,
                };
                if (mk != null)
                {
                    menuKey = mk;
                    continue;
                }
                input |= key.Trim() switch
                {
                    "left" => ShipInput.Left,
                    "right" => ShipInput.Right,
                    "thrust" => ShipInput.Thrust,
                    "action" => ShipInput.Action,
                    "none" => ShipInput.None,
                    var k => throw new ArgumentException($"Unknown input '{k}' in --script"),
                };
            }
            steps.Add(new ScriptStep(int.Parse(kv[1]), input, menuKey));
        }
        return steps;
    }
}
