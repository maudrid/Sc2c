// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/zoqfot/zoqfotc.c:
// SelectAlienZOQ, SelectAlienPIK, ZFPTalkSegue), Copyright Paul Reiche,
// Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors,
// under the GNU GPL 2 or later.

using Crateris.Core.Data;
using Crateris.Core.Systems;

namespace Crateris.Presentation;

/// <summary>Where a speaker's lines are drawn on the communication screen: CommData's AlienTextBaseline and its colours.</summary>
public readonly record struct CommTextPlace(int X, int Y, Rgb Fore, Rgb Back);

/// <summary>
/// The speakers a race's lines switch between on the communication screen
/// (stage 3b-2 item 5): what each line's callback (NPCPhrase_cb, sent with
/// the line: <see cref="Core.Comm.TalkView.Speakers"/>) changes as its pages
/// play, run on the client as UQM's track player runs it on the alien's
/// screen. Only the Zoq-Fot-Pik have them (<see cref="For"/>).
/// </summary>
public abstract class CommSpeakers
{
    /// <summary>The speakers of a script's talk, or null for a script whose lines have no callbacks.</summary>
    public static CommSpeakers? For(string script) => script == "zoqfot" ? new ZoqFotPikSpeakers() : null;

    /// <summary>Where the speaker's text goes now, or null for the race's description's place.</summary>
    public CommTextPlace? Text { get; protected set; }

    /// <summary>A run of lines starts playing (the script's segue: <see cref="ZoqFotPikSpeakers"/>' ZFPTalkSegue).</summary>
    public abstract void Segue(AlienAnimation animation);

    /// <summary>A page of a line with this callback starts playing.</summary>
    public abstract void Run(string callback, AlienAnimation animation);
}

/// <summary>
/// zoqfotc.c's two speakers: Zoq on the left and Pik on the right, each
/// with his own talking frames and the eye's move to him (the transition),
/// Fot's smoke (the second ambient animation) waiting while Pik talks;
/// LastAlien keeps who spoke last, so a callback changes nothing for the
/// speaker already talking. Each run of lines (ZFPTalkSegue) starts with
/// Fot looking on, then Zoq selected.
/// </summary>
public sealed class ZoqFotPikSpeakers : CommSpeakers
{
    // zoqfotc.c's ZOQ_ALIEN, FOT_ALIEN, PIK_ALIEN.
    private const int Zoq = 0, Fot = 1, Pik = 2;

    // TEXT_X_OFFS and SIS_TEXT_WIDTH (comm.h), SIS_SCREEN_WIDTH (units.h).
    private const int TextXOffs = 1, SisScreenWidth = 242, SisTextWidth = SisScreenWidth - (TextXOffs << 1);

    /// <summary>ZOQ_BASE_X, PIK_BASE_X, and both speakers' BASE_Y.</summary>
    public const int ZoqBaseX = TextXOffs + ((SisTextWidth >> 1) >> 1), PikBaseX = SisScreenWidth - ZoqBaseX, BaseY = 24;

    // ZOQ_TALK_INDEX, ZOQ_TALK_FRAMES, FOT_TO_ZOQ; PIK_TALK_INDEX, PIK_TALK_FRAMES, FOT_TO_PIK.
    private const int ZoqTalkIndex = 18, ZoqTalkFrames = 5, FotToZoq = 23, PikTalkIndex = 29, PikTalkFrames = 2, FotToPik = 26;

    // WHITE_COLOR and BLACK_COLOR: both speakers' text and its back.
    private static readonly Rgb White = new(0xFF, 0xFF, 0xFF), Black = new(0, 0, 0);

    // The static LastAlien; ZOQ_ALIEN (0) before any talk, as UQM's starts.
    private int _lastAlien = Zoq;

    public override void Segue(AlienAnimation animation)
    {
        _lastAlien = Fot;
        Select(Zoq, animation);
    }

    public override void Run(string callback, AlienAnimation animation)
    {
        switch (callback)
        {
            case "SelectAlienZOQ":
                Select(Zoq, animation);
                break;
            case "SelectAlienPIK":
                Select(Pik, animation);
                break;
        }
    }

    // SelectAlienZOQ and SelectAlienPIK: the transition to neutral first if
    // the other was talking, then the eye's move to the speaker, his talking
    // frames, Fot's smoke waiting or not, and his text's place.
    private void Select(int who, AlienAnimation animation)
    {
        if (_lastAlien == who)
            return;
        bool done = _lastAlien != Fot;
        _lastAlien = who;
        bool zoq = who == Zoq;
        animation.SelectTalking(zoq ? ZoqTalkIndex : PikTalkIndex, zoq ? ZoqTalkFrames : PikTalkFrames,
            zoq ? FotToZoq : FotToPik, done);
        var smoke = animation.AmbientFlags[1];
        animation.SetAmbientFlags(1, zoq ? smoke & ~CommAnimationFlags.WaitTalking : smoke | CommAnimationFlags.WaitTalking);
        Text = new CommTextPlace(zoq ? ZoqBaseX : PikBaseX, BaseY, White, Black);
    }
}
