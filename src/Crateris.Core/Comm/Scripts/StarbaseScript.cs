// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/starbas/starbas.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The allied starbase commander's talk (starbas.c), so far as the opening
/// reaches it (stage 3a, item 5): "the starbase is ready", with its 2,500
/// resource units, the date noted for the bulletins, and the goodbye. The
/// rest of the talk (the mission, the history, the races, the devices,
/// the bulletins and selling minerals) is item 8's; until then this talk
/// offers only the goodbye after the starbase-ready lines, where UQM
/// also offers to sell minerals and to give information.
/// </summary>
public sealed class StarbaseScript : CommScript
{
    // CurBulletinMask: the bulletins given this talk (none yet: item 8).
    private uint _curBulletinMask;

    public StarbaseScript(ConversationScript phrases) : base(phrases)
    {
    }

    private void ByeBye(int r)
    {
        _curBulletinMask |= T.Get32("STARBASE_BULLETS0");
        T.Set32("STARBASE_BULLETS0", _curBulletinMask);

        if (GetGameState("CHMMR_BOMB_STATE") >= 2)
            NPCPhrase("GOOD_LUCK_AGAIN");
        else
        {
            string letter = "ABCDEFGH"[(int)((byte)T.Random() & 7)].ToString();
            NPCPhrase($"NORMAL_GOODBYE_{letter}0");
            if (!T.UsingSpeech)
            {
                NPCPhrase("SPACE");
                NPCPhrase(Talk.GlobalPlayerName);
            }
            NPCPhrase($"NORMAL_GOODBYE_{letter}1");
        }
    }

    private void NormalStarbase(int r)
    {
        if (r != 0)
            throw new NotSupportedException("the allied commander's replies come with stage 3a's item 8");
        if (GetGameState("MOONBASE_ON_SHIP") == 0)
            throw new NotSupportedException("the allied commander's greetings and bulletins come with stage 3a's item 8");

        NPCPhrase("STARBASE_IS_READY_A");
        if (T.UsingSpeech)
            NPCPhrase("YOUR_FLAGSHIP_3DO1");
        else
        {
            NPCPhrase("YOUR_FLAGSHIP_PC");
            NPCPhrase(Talk.GlobalShipName);
        }
        NPCPhrase("STARBASE_IS_READY_B");
        if (T.UsingSpeech)
            NPCPhrase("YOUR_FLAGSHIP_3DO0");
        else
            NPCPhrase(Talk.GlobalShipName);
        NPCPhrase("STARBASE_IS_READY_C");
        T.DeltaSISGauges(0, 0, 2500);
        SetGameState("STARBASE_MONTH", T.Clock.Month);
        SetGameState("STARBASE_DAY", T.Clock.Day);

        SetGameState("STARBASE_VISITED", 1);

        // UQM offers have_minerals (with minerals aboard), new_devices (with
        // a device not yet discussed), repeat_bulletins and need_info here
        // too: item 8.
        Response("goodbye_commander", ByeBye);
    }

    protected internal override void Intro() => NormalStarbase(0);

    protected internal override void PostEncounter()
    {
        SetGameState("MOONBASE_ON_SHIP", 0);
        if (GetGameState("CHMMR_BOMB_STATE") == 2)
            SetGameState("CHMMR_BOMB_STATE", 3);
    }

    protected internal override void Init()
    {
        // AlienTextWidth and AlienTextBaseline: in the description already.
        // The alternate starbase track, if there is one.
        T.AltSong = "comm.starbase.music";
        T.UseAltSong = true;

        _curBulletinMask = 0;
        T.SetSegue(Segue.Peace);
    }
}
