// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Hyperspace;
using Crateris.Core.Systems;

namespace Crateris.Core.Session;

/// <summary>
/// Scripted input instead of keys (--script, for tests): steps like
/// "thrust:40,right+thrust:10,down:2,none:20", each held for some frames.
/// left, right, thrust, action, fire, takeoff, special and none are ship
/// input; up, down, select, cancel, pageup and pagedown press an orbit menu
/// key once as their step starts, give opens or closes the giving screen
/// (as T does) then, and pause is the host's pause key.
/// </summary>
public sealed class InputScript
{
    /// <summary>One step: input held for some frames; a menu key is pressed once as the step starts.</summary>
    public sealed record Step(int Frames, ShipInput Input, MenuKey? Key = null, bool Give = false, bool Pause = false);

    private readonly IReadOnlyList<Step> _steps;
    private int _step;
    private int _frame;

    public InputScript(IReadOnlyList<Step> steps) => _steps = steps;

    public IReadOnlyList<Step> Steps => _steps;

    /// <summary>Whether every step has run.</summary>
    public bool Done
    {
        get
        {
            Skip();
            return _step >= _steps.Count;
        }
    }

    public static InputScript Parse(string text)
    {
        var steps = new List<Step>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split(':');
            if (kv.Length != 2 || !int.TryParse(kv[1], out int frames) || frames < 1)
                throw new ArgumentException($"A script step is name:frames, with at least one frame, not '{part}'");
            var input = ShipInput.None;
            MenuKey? menuKey = null;
            bool give = false, pause = false;
            foreach (var key in kv[0].Split('+'))
            {
                if (key.Trim() == "give")
                {
                    give = true;
                    continue;
                }
                // The host's pause key (stage 4a item 10): pauses, or lifts the pause; a guest's does nothing.
                if (key.Trim() == "pause")
                {
                    pause = true;
                    continue;
                }
                MenuKey? mk = key.Trim() switch
                {
                    "up" => MenuKey.Up,
                    "down" => MenuKey.Down,
                    "select" => MenuKey.Select,
                    "cancel" => MenuKey.Cancel,
                    "pageup" => MenuKey.PageUp,
                    "pagedown" => MenuKey.PageDown,
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
                    "fire" => ShipInput.Fire,
                    "takeoff" => ShipInput.TakeOff,
                    "special" => ShipInput.Special,
                    "none" => ShipInput.None,
                    var k => throw new ArgumentException($"Unknown input '{k}' in --script"),
                };
            }
            steps.Add(new Step(frames, input, menuKey, give, pause));
        }
        return new InputScript(steps);
    }

    /// <summary>
    /// The next frame's input, and the menu key its step presses if this is
    /// the step's first frame. After the last step, no input.
    /// </summary>
    public ShipInput Next(out MenuKey? press) => Next(out press, out _, out _);

    /// <summary>The next frame's input, as <see cref="Next(out MenuKey?)"/>, and whether its step opens or closes the giving screen.</summary>
    public ShipInput Next(out MenuKey? press, out bool give) => Next(out press, out give, out _);

    /// <summary>The next frame's input, as <see cref="Next(out MenuKey?)"/>, whether its step opens or closes the giving screen, and whether it presses the pause key.</summary>
    public ShipInput Next(out MenuKey? press, out bool give, out bool pause)
    {
        press = null;
        give = false;
        pause = false;
        Skip();
        if (_step >= _steps.Count)
            return ShipInput.None;
        var step = _steps[_step];
        if (_frame++ == 0)
            (press, give, pause) = (step.Key, step.Give, step.Pause);
        return step.Input;
    }

    private void Skip()
    {
        while (_step < _steps.Count && _frame >= _steps[_step].Frames)
        {
            _step++;
            _frame = 0;
        }
    }
}
