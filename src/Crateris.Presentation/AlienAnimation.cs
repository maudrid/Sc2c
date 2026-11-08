// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/commanim.c; comm.h's
// talking flags; comm.c's TalkSegue's part in them), Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;
using Crateris.Core.Uqm;

namespace Crateris.Presentation;

/// <summary>A colour fade an animation starts: to colour map <see cref="Map"/> over <see cref="Time"/> ticks (XFormColorMap).</summary>
public readonly record struct AlienColorFade(int Map, int Time);

/// <summary>
/// The alien's animations in the communication screen, as UQM's
/// commanim.c runs them (ProcessCommAnimations): the ambient animations
/// (picture or colour map, random, circular or yo-yo, blocking each other
/// and waiting for the talking), the talking animation and its transition.
/// Each <see cref="Step"/> is a frame of the screen, so many ticks
/// (ONE_SECOND 840) on; it gives the frames to stamp, over what was drawn
/// before (the whole picture on a full redraw), and the colour fades to
/// start. Each client runs its own, with a generator of its own (stage 3,
/// decision 12): the animations don't change the talk.
/// </summary>
public sealed class AlienAnimation
{
    /// <summary>The main frame (CommData.AlienFrame) in <see cref="Frames"/>; others are frame indexes.</summary>
    public const int MainFrame = -1;

    /// <summary>COMM_ANIM_RATE: the screen's frame, ONE_SECOND / 40 ticks.</summary>
    public const int FrameTicks = 840 / 40;

    private const CommAnimationFlags AnimMask = CommAnimationFlags.Random | CommAnimationFlags.Circular | CommAnimationFlags.YoYo;
    private const CommAnimationFlags ColorXform = CommAnimationFlags.PauseTalking;
    private const CommAnimationFlags OneShot = CommAnimationFlags.TalkIntro;

    private enum Kind { Picture, Color }

    private sealed class Sequence
    {
        public Desc D = null!;
        public uint Alarm;
        public int Direction;
        public int CurIndex, NextIndex, FramesLeft;
        public Kind Type;
        public bool Change;
    }

    // ANIMATION_DESC, whose flags change as it runs.
    private sealed class Desc
    {
        public int StartIndex, NumFrames, BaseFrameRate, RandomFrameRate, BaseRestartRate, RandomRestartRate;
        public CommAnimationFlags Flags;
        public uint BlockMask;

        public static Desc Of(Core.Data.CommAnimation a) => new()
        {
            StartIndex = a.StartIndex, NumFrames = (byte)a.NumFrames, Flags = (CommAnimationFlags)(byte)a.Flags,
            BaseFrameRate = (ushort)a.BaseFrameRate, RandomFrameRate = (ushort)a.RandomFrameRate,
            BaseRestartRate = (ushort)a.BaseRestartRate, RandomRestartRate = (ushort)a.RandomRestartRate,
            BlockMask = a.BlockMask,
        };

        public Desc Copy() => (Desc)MemberwiseClone();
    }

    private readonly TfbRandom _random;
    private readonly Desc[] _ambient;
    // CommData's talking and transition descriptors (the script's and
    // TalkSegue's flags), and commanim's own copies (TalkDesc, TransitDesc).
    private readonly Desc _talkData, _transitData;
    private readonly Sequence[] _sequences;
    private readonly Sequence _talk, _transit;
    private uint _activeMask;
    private bool _first = true;

    /// <param name="ambient">The race's ambient animations (CommData.AlienAmbientArray).</param>
    /// <param name="seed">The client's own seed for the animations' random numbers.</param>
    public AlienAnimation(IReadOnlyList<Core.Data.CommAnimation> ambient, Core.Data.CommAnimation talk,
        Core.Data.CommAnimation transition, uint seed)
    {
        _random = new TfbRandom(seed);
        _ambient = ambient.Select(Desc.Of).ToArray();
        _talkData = Desc.Of(talk);
        _transitData = Desc.Of(transition);
        // InitCommAnimations: the transition first, then the talking, then
        // the ambient ones (drawn in reverse, so the talking goes over them).
        _sequences = new Sequence[2 + _ambient.Length];
        _transit = _sequences[0] = TalkSequence(_transitData.Copy());
        _talk = _sequences[1] = TalkSequence(_talkData.Copy());
        for (int i = 0; i < _ambient.Length; ++i)
            _sequences[2 + i] = AmbientSequence(_ambient[i]);
    }

    /// <summary>The ambient animations' flags now (a script may change them: <see cref="SetAmbientFlags"/>).</summary>
    public IReadOnlyList<CommAnimationFlags> AmbientFlags => _ambient.Select(a => a.Flags).ToList();

    /// <summary>The talking descriptor's flags (CommData.AlienTalkDesc.AnimFlags).</summary>
    public CommAnimationFlags TalkFlags => _talkData.Flags;

    /// <summary>The transition descriptor's flags (CommData.AlienTransitionDesc.AnimFlags).</summary>
    public CommAnimationFlags TransitionFlags => _transitData.Flags;

    /// <summary>A script set an ambient animation's flags (CommData.AlienAmbientArray[i].AnimFlags).</summary>
    public void SetAmbientFlags(int i, CommAnimationFlags flags) => _ambient[i].Flags = (CommAnimationFlags)(byte)flags;

    /// <summary>The script doesn't want the talking (PAUSE_TALKING set), or wants it again.</summary>
    public void PauseTalking(bool pause)
    {
        if (pause)
            _talkData.Flags |= CommAnimationFlags.PauseTalking;
        else
            _talkData.Flags &= ~CommAnimationFlags.PauseTalking;
    }

    /// <summary>TalkSegue starts the lines: the alien talks (after its intro, if it has one), unless the script paused the talking.</summary>
    public void StartTalking()
    {
        if ((_talkData.Flags & CommAnimationFlags.PauseTalking) != 0 || _talkData.NumFrames == 0)
            return;
        if (_transitData.NumFrames > 0)
            _transitData.Flags |= CommAnimationFlags.TalkIntro;
        _talkData.Flags |= CommAnimationFlags.WaitTalking;
    }

    /// <summary>TalkSegue's lines are done: the talking stops (through its transition back).</summary>
    public void StopTalking()
    {
        if (Running)
            _talkData.Flags |= CommAnimationFlags.TalkDone;
    }

    /// <summary>Whether the talking animation runs (runningTalkingAnim): TalkSegue waits for it to stop.</summary>
    public bool Running => (_talkData.Flags & CommAnimationFlags.WaitTalking) != 0;

    /// <summary>Whether the intro transition is still running (runningIntroAnim): the lines wait for it.</summary>
    public bool Intro => (_transitData.Flags & CommAnimationFlags.TalkIntro) != 0;

    private uint FrameRate(Sequence s) => (uint)(s.D.BaseFrameRate + _random.Next() % (uint)(s.D.RandomFrameRate + 1));

    private uint RestartRate(Sequence s) => (uint)(s.D.BaseRestartRate + _random.Next() % (uint)(s.D.RandomRestartRate + 1));

    private int FrameIndex(Sequence s, int from) => (ushort)(from + _random.Next() % (uint)(s.D.NumFrames - from));

    private Sequence AmbientSequence(Desc d)
    {
        var s = new Sequence
        {
            D = d,
            Type = (d.Flags & ColorXform) != 0 ? Kind.Color : Kind.Picture,
            Direction = 1,
            FramesLeft = d.NumFrames,
        };
        if ((d.Flags & CommAnimationFlags.Random) != 0)
            s.NextIndex = (int)(_random.Next() % (uint)d.NumFrames);
        else if ((d.Flags & CommAnimationFlags.YoYo) != 0)
        {
            s.NextIndex = 1;
            --s.FramesLeft;
        }
        else if ((d.Flags & CommAnimationFlags.Circular) != 0)
        {
            s.CurIndex = d.NumFrames - 1;
            s.NextIndex = 0;
        }
        s.Alarm = RestartRate(s) + 1;
        return s;
    }

    private static Sequence TalkSequence(Desc d)
    {
        d.Flags |= CommAnimationFlags.Disabled;
        return new Sequence { D = d, Type = Kind.Picture };
    }

    private static bool AtNeutral(Sequence s) =>
        (s.D.Flags & CommAnimationFlags.Circular) != 0 ? s.NextIndex == 0 : s.CurIndex == 0;

    private bool ConflictsWithTalking(Sequence s) => (s.D.Flags & _talkData.Flags & CommAnimationFlags.WaitTalking) != 0;

    private bool AdvanceAmbient(Sequence s)
    {
        var d = s.D;
        bool active;
        --s.FramesLeft;
        if (s.FramesLeft != 0 || ((d.Flags & CommAnimationFlags.YoYo) != 0 && s.NextIndex != 0))
        {
            active = true;
            s.Alarm = FrameRate(s) + 1;
        }
        else
        {
            active = false;
            s.Alarm = RestartRate(s) + 1;
            if ((d.Flags & CommAnimationFlags.Random) != 0)
                s.NextIndex = 0;
        }
        s.CurIndex = s.NextIndex;
        s.Change = true;
        if (s.FramesLeft == 0)
        {
            s.FramesLeft = d.NumFrames;
            if ((d.Flags & CommAnimationFlags.YoYo) != 0)
            {
                --s.FramesLeft;
                s.Direction = -s.Direction;
            }
            else if ((d.Flags & CommAnimationFlags.Circular) != 0)
                s.NextIndex = -1;
        }
        if ((d.Flags & CommAnimationFlags.Random) != 0)
            s.NextIndex = FrameIndex(s, 0);
        else
            s.NextIndex = (ushort)(s.NextIndex + s.Direction);
        return active;
    }

    private static void Reset(Sequence s)
    {
        s.Direction = 0;
        s.CurIndex = 0;
        s.Change = true;
    }

    private void AdvanceTalking(Sequence s, uint elapsed)
    {
        var d = s.D;
        if (s.Direction == 0)
        {
            s.Direction = 1;
            if (_talkData.StartIndex != d.StartIndex)
            {
                d = s.D = _talkData.Copy();
            }
            s.Alarm = 0;
            d.Flags &= ~CommAnimationFlags.Disabled;
        }
        if (s.Alarm > elapsed)
        {
            s.Alarm -= elapsed;
            return;
        }
        s.Alarm = FrameRate(s);
        s.Change = true;
        if (s.CurIndex == 0)
        {
            s.CurIndex = FrameIndex(s, 1);
            s.Alarm += RestartRate(s);
        }
        else
            s.CurIndex = 0;
    }

    private bool AdvanceTransit(Sequence s, uint elapsed)
    {
        bool done = false;
        var d = s.D;
        if (s.Direction == 0)
        {
            s.Alarm = 0;
            d.Flags &= ~CommAnimationFlags.Disabled;
        }
        if (s.Alarm > elapsed)
        {
            s.Alarm -= elapsed;
            return false;
        }
        s.Change = true;
        if (s.Direction == 0)
        {
            s.FramesLeft = d.NumFrames;
            if ((_transitData.Flags & CommAnimationFlags.TalkDone) != 0)
            {
                s.Direction = -1;
                s.CurIndex = d.NumFrames - 1;
            }
            else if ((_transitData.Flags & CommAnimationFlags.TalkIntro) != 0)
            {
                s.Direction = 1;
                if (_transitData.StartIndex != d.StartIndex)
                    d = s.D = _transitData.Copy();
                s.CurIndex = 0;
            }
        }
        --s.FramesLeft;
        if (s.FramesLeft == 0)
        {
            if (s.Direction == 1)
                _transitData.Flags &= ~CommAnimationFlags.TalkIntro;
            else if (s.Direction == -1)
            {
                _transitData.Flags &= ~CommAnimationFlags.TalkDone;
                d.Flags |= CommAnimationFlags.Disabled;
                done = true;
            }
            s.Direction = 0;
        }
        else
        {
            s.Alarm = FrameRate(s);
            s.CurIndex = (ushort)(s.CurIndex + s.Direction);
        }
        return done;
    }

    /// <summary>
    /// A frame of the screen, <paramref name="ticks"/> on (ProcessCommAnimations):
    /// the frames to stamp in order (<see cref="MainFrame"/> for the main
    /// one; the first step redraws the whole picture) and the colour fades
    /// to start.
    /// </summary>
    public (List<int> Frames, List<AlienColorFade> Fades) Step(int ticks)
    {
        uint elapsed = (uint)ticks;
        bool canTalk = true;
        uint nextActive = _activeMask;
        for (int i = 0; i < _ambient.Length; ++i)
        {
            var s = _sequences[2 + i];
            var d = s.D;
            uint bit = 1u << i;
            if ((d.Flags & CommAnimationFlags.Disabled) != 0)
                continue;
            if (s.Direction == 0)
            {
                if (!ConflictsWithTalking(s))
                    s.Direction = 1;
            }
            else if (s.Alarm > elapsed)
                s.Alarm -= elapsed;
            else if ((_activeMask & d.BlockMask) != 0)
            {
                s.Alarm = RestartRate(s) + 1;
                continue;
            }
            else if (AdvanceAmbient(s))
            {
                _activeMask |= bit;
                nextActive |= bit;
            }
            else
                nextActive &= ~bit;

            if (s.Type == Kind.Picture && s.Direction != 0 && ConflictsWithTalking(s))
            {
                if (AtNeutral(s))
                {
                    s.Direction = 0;
                    nextActive &= ~bit;
                }
                else
                    canTalk = false;
            }
        }
        _activeMask = nextActive;

        bool haveTalk = _talkData.NumFrames > 0, haveTransit = _transitData.NumFrames > 0;
        if (canTalk && haveTalk && Running)
        {
            bool done = false;
            bool stop = (_talkData.Flags & CommAnimationFlags.TalkDone) != 0;
            if (stop && haveTransit)
                _transitData.Flags |= CommAnimationFlags.TalkDone;
            if ((_transitData.Flags & (CommAnimationFlags.TalkIntro | CommAnimationFlags.TalkDone)) != 0)
            {
                if ((_transitData.Flags & CommAnimationFlags.TalkDone) != 0 && _transit.Direction == 0)
                    Reset(_talk);
                done = AdvanceTransit(_transit, elapsed);
            }
            else if (!stop)
                AdvanceTalking(_talk, elapsed);
            else
            {
                Reset(_talk);
                done = true;
            }
            if (stop && done)
                _talkData.Flags &= ~(CommAnimationFlags.WaitTalking | CommAnimationFlags.TalkDone);
        }
        else if (_talk.Direction == 0)
            _talk.D.Flags |= CommAnimationFlags.Disabled;

        // The colour maps' animations, then the pictures (DrawAlienFrame).
        var fades = new List<AlienColorFade>();
        for (int i = 0; i < _ambient.Length; ++i)
        {
            var s = _sequences[2 + i];
            if ((s.D.Flags & CommAnimationFlags.Disabled) != 0 || s.Type != Kind.Color || !s.Change)
                continue;
            fades.Add(new AlienColorFade(s.D.StartIndex + s.CurIndex, (short)(s.Alarm - 1)));
            s.Change = false;
        }
        var frames = new List<int>();
        bool full = _first;
        _first = false;
        if (full)
        {
            frames.Add(MainFrame);
            for (int i = _ambient.Length - 1; i >= 0; --i)
            {
                var d = _ambient[i];
                if ((d.Flags & AnimMask) != 0)
                    continue;
                d.Flags |= CommAnimationFlags.Disabled;
                if ((d.Flags & ColorXform) == 0)
                    frames.Add(d.StartIndex);
            }
        }
        for (int i = _sequences.Length - 1; i >= 0; --i)
        {
            var s = _sequences[i];
            if ((s.D.Flags & CommAnimationFlags.Disabled) != 0 || s.Type != Kind.Picture)
                continue;
            if (!full && !s.Change)
                continue;
            frames.Add((ushort)(s.D.StartIndex + s.CurIndex));
            s.Change = false;
        }
        // A one-shot animation, inactive next frame, is done.
        for (int i = 0; i < _ambient.Length; ++i)
        {
            var d = _ambient[i];
            if ((d.Flags & CommAnimationFlags.Disabled) == 0 && (d.Flags & OneShot) != 0 && (nextActive & (1u << i)) == 0)
                d.Flags |= CommAnimationFlags.Disabled;
        }
        return (frames, fades);
    }
}
