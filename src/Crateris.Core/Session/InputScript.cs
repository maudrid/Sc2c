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
/// left, right, thrust, action and none are ship input; up, down, select
/// and cancel press an orbit menu key once as their step starts.
/// </summary>
public sealed class InputScript
{
    /// <summary>One step: input held for some frames; a menu key is pressed once as the step starts.</summary>
    public sealed record Step(int Frames, ShipInput Input, MenuKey? Key = null);

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
                    "fire" => ShipInput.Fire,
                    "none" => ShipInput.None,
                    var k => throw new ArgumentException($"Unknown input '{k}' in --script"),
                };
            }
            steps.Add(new Step(frames, input, menuKey));
        }
        return new InputScript(steps);
    }

    /// <summary>
    /// The next frame's input, and the menu key its step presses if this is
    /// the step's first frame. After the last step, no input.
    /// </summary>
    public ShipInput Next(out MenuKey? press)
    {
        press = null;
        Skip();
        if (_step >= _steps.Count)
            return ShipInput.None;
        var step = _steps[_step];
        if (_frame++ == 0)
            press = step.Key;
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
