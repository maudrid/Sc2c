// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/zoqfot/zoqfotc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Zoq-Fot-Pik (zoqfotc.c), whole: every line Zoq's or Pik's, queued
/// with its speaker's callback (NPCPhrase_cb: <see cref="SelectAlienZOQ"/>,
/// <see cref="SelectAlienPIK"/>), which runs as the line plays and which a
/// client follows to switch the speaker on the communication screen (their
/// text's place and colour, the talking frames, the eye's move: the
/// presentation's, as the callbacks change nothing a talk records); each
/// set of lines played by ZFPTalkSegue, Zoq speaking first. The first
/// meeting (MET_ZOQFOT; at their homeworld, GLOBAL_FLAGS_AND_DATA bit 7,
/// or their scout, genzfpscout.c's own group, which then flies off), its
/// introductions and the alliance offered; their hostility after
/// "all very interesting" or "never" (ZOQFOT_HOSTILE: every later talk a
/// fight); the neutral and allied homeworld's greetings (ZOQFOT_HOME_VISITS),
/// its news (ZOQFOT_INFO), the war's news by the date, the Utwig and Supox's
/// mission and the Kohr-Ah's frenzy (ZOQFOT_KNOW_MASK), the allies'
/// questions, and the alliance made (SetRaceAllied; the distress event it
/// queues, ZOQFOT_DISTRESS_EVENT, set aside until stage 3c); the thanks for
/// a rescue from distress (3c's) with 4 ships to the host's fleet. Every
/// path is checked against UQM's own script (tests/golden/talks-zoqfot.json).
/// </summary>
public sealed class ZoqFotPikScript : CommScript
{
    // races.h's ZOQFOTPIK_SHIP.
    private const int ZoqFotPikShip = 19;

    // MAX_ZFP_SHIPS: the ships a rescue gives.
    private const int MaxZfpShips = 4;

    // ZOQFOT_KNOW_MASK's bits (any_war_news).
    private const int UtwigBuyTime = 1 << 0, KohrAhWin = 1 << 1, UrquanLose = 1 << 2, KohrAhKill = 1 << 3;
    private const int KnowAll = UtwigBuyTime | KohrAhWin | UrquanLose | KohrAhKill;

    // clock.h's START_YEAR and globdata.h's YEARS_TO_KOHRAH_VICTORY.
    private const int StartYear = 2155, YearsToKohrAhVictory = 4;

    // LastAlien's values: who spoke last (the callbacks switch only when it changes).
    private const int ZoqAlien = 0, FotAlien = 1, PikAlien = 2;

    // zoqfotc.c's static LastAlien. The callbacks' changes are the
    // communication screen's (CommData's text place and colours, the
    // talking and transition frames), which the client makes as each line
    // plays (the line's callback, sent with it); here they keep who spoke
    // last and the one change a talk records, the second ambient
    // animation's WAIT_TALKING (the screen's flags, as UQM's recording
    // has them).
    private int _lastAlien;

    public ZoqFotPikScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    /// <summary>SelectAlienZOQ: Zoq speaks (run as each page of his lines plays).</summary>
    private void SelectAlienZOQ()
    {
        if (_lastAlien != ZoqAlien)
        {
            _lastAlien = ZoqAlien;
            // Fot's smoke (the second ambient animation) blows on while Zoq talks.
            T.Ambient[1] &= ~Data.CommAnimationFlags.WaitTalking;
        }
    }

    /// <summary>SelectAlienPIK: Pik speaks (run as each page of his lines plays).</summary>
    private void SelectAlienPIK()
    {
        if (_lastAlien != PikAlien)
        {
            _lastAlien = PikAlien;
            // ... and waits while Pik talks.
            T.Ambient[1] |= Data.CommAnimationFlags.WaitTalking;
        }
    }

    // ZFPTalkSegue: Fot looking on, then Zoq selected, and the lines played.
    private void ZFPTalkSegue(int waitTrack)
    {
        _lastAlien = FotAlien;
        SelectAlienZOQ();
        T.AlienTalkSegue(waitTrack);
    }

    // NPCPhrase_cb (ID0, &SelectAlienZOQ); NPCPhrase_cb (ID1, &SelectAlienPIK); ...
    // for ID0 to ID(count - 1): every script's run of lines starts with Zoq
    // and alternates.
    private void Lines(string prefix, int count)
    {
        for (int i = 0; i < count; ++i)
            T.NPCPhrase(prefix + i, i % 2 == 0 ? new Action(SelectAlienZOQ) : SelectAlienPIK);
    }

    // A run of lines, then ZFPTalkSegue ((COUNT)~0).
    private void Say(string prefix, int count)
    {
        Lines(prefix, count);
        ZFPTalkSegue(Talk.WaitAll);
    }

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Peace);

        if (PlayerSaid(r, "bye_homeworld"))
            Say("GOODBYE_HOME", 2);
        else if (PlayerSaid(r, "decide_later"))
            Say("PLEASE_HURRY", 2);
        else if (PlayerSaid(r, "valuable_info"))
            Say("GOODBYE", 4);
        else if (PlayerSaid(r, "how_can_i_help"))
            Say("EMMISSARIES", 8);
        else if (PlayerSaid(r, "sure"))
        {
            Say("WE_ALLY", 6);
            T.SetRaceAllied(ZoqFotPikShip, true);
            // The distress event: set aside until stage 3c runs the calendar.
            T.AddEvent(relative: true, 3, 0, 0, "ZOQFOT_DISTRESS_EVENT");
            SetGameState("ZOQFOT_HOME_VISITS", 0);
        }
        else if (PlayerSaid(r, "all_very_interesting"))
        {
            Say("SEE_TOLD_YOU", 4);

            SetGameState("ZOQFOT_HOSTILE", 1);
            SetGameState("ZOQFOT_HOME_VISITS", 0);
            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "never"))
        {
            Say("WE_ENEMIES", 2);

            SetGameState("ZOQFOT_HOME_VISITS", 0);
            SetGameState("ZOQFOT_HOSTILE", 1);
            T.SetSegue(Segue.Hostile);
        }
    }

    private void FormAlliance(int r)
    {
        Say("ALLY_WITH_US", 6);

        Response("sure", ExitConversation);
        Response("never", ExitConversation);
        Response("decide_later", ExitConversation);
    }

    private void ZoqFotIntro(int r)
    {
        if (PlayerSaid(r, "we_are_vindicator0"))
        {
            Say("WE_GLAD", 6);

            DisablePhrase("we_are_vindicator0");
        }
        else if (PlayerSaid(r, "your_race"))
        {
            Say("YEARS_AGO", 14);

            DisablePhrase("your_race");
        }
        else if (PlayerSaid(r, "where_from"))
        {
            Say("TRAVELED_FAR", 6);

            DisablePhrase("where_from");
        }
        else if (PlayerSaid(r, "what_emergency"))
        {
            Say("UNDER_ATTACK", 12);

            DisablePhrase("what_emergency");
        }
        else if (PlayerSaid(r, "tough_luck"))
        {
            Say("NOT_HELPFUL", 6);

            DisablePhrase("tough_luck");
        }
        else if (PlayerSaid(r, "what_look_like"))
        {
            Say("LOOK_LIKE", 4);

            DisablePhrase("what_look_like");
        }

        if (PhraseEnabled("your_race") || PhraseEnabled("where_from") || PhraseEnabled("what_emergency"))
        {
            if (PhraseEnabled("your_race"))
                Response("your_race", ZoqFotIntro);
            if (PhraseEnabled("where_from"))
                Response("where_from", ZoqFotIntro);
            if (PhraseEnabled("what_emergency"))
                Response("what_emergency", ZoqFotIntro);
        }
        else
        {
            if (PhraseEnabled("tough_luck"))
                Response("tough_luck", ZoqFotIntro);
            if (PhraseEnabled("what_look_like"))
                Response("what_look_like", ZoqFotIntro);
            Response("all_very_interesting", ExitConversation);
            if ((GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0)
                Response("how_can_i_help", FormAlliance);
            else
                Response("how_can_i_help", ExitConversation);
            Response("valuable_info", ExitConversation);
        }
    }

    private void AquaintZoqFot(int r)
    {
        if (PlayerSaid(r, "which_fot"))
        {
            Say("HE_IS", 8);

            DisablePhrase("which_fot");
        }
        else if (PlayerSaid(r, "quiet_toadies"))
        {
            Say("TOLD_YOU", 8);

            DisablePhrase("quiet_toadies");
        }

        if (PhraseEnabled("we_are_vindicator0"))
        {
            T.ConstructResponse("we_are_vindicator0", T.GetAllianceName("name_1"), "we_are_vindicator1",
                Sis.ShipName, "we_are_vindicator2");
        }

        if (PhraseEnabled("which_fot"))
            Response("which_fot", AquaintZoqFot);
        if (PhraseEnabled("we_are_vindicator0"))
            T.DoResponsePhrase("we_are_vindicator0", ZoqFotIntro, Talk.SharedPhraseBuffer);
        if (PhraseEnabled("quiet_toadies"))
            Response("quiet_toadies", AquaintZoqFot);
        Response("all_very_interesting", ExitConversation);
        Response("valuable_info", ExitConversation);
    }

    private void ZoqFotInfo(int r)
    {
        if (PlayerSaid(r, "want_specific_info"))
            Say("WHAT_SPECIFIC_INFO", 2);
        else if (PlayerSaid(r, "what_about_others"))
        {
            Say("ABOUT_OTHERS", 14);

            DisablePhrase("what_about_others");
        }
        else if (PlayerSaid(r, "what_about_zebranky"))
        {
            Say("ABOUT_ZEBRANKY", 8);

            DisablePhrase("what_about_zebranky");
        }
        else if (PlayerSaid(r, "what_about_stinger"))
        {
            Say("ABOUT_STINGER", 6);

            DisablePhrase("what_about_stinger");
        }
        else if (PlayerSaid(r, "what_about_guy_in_back"))
        {
            Say("ABOUT_GUY", 2);

            DisablePhrase("what_about_guy_in_back");
        }
        else if (PlayerSaid(r, "what_about_past"))
        {
            Say("ABOUT_PAST", 12);

            DisablePhrase("what_about_past");
        }

        bool infoLeft = false;
        foreach (var id in new[] { "what_about_others", "what_about_zebranky", "what_about_stinger",
                     "what_about_guy_in_back", "what_about_past" })
        {
            if (PhraseEnabled(id))
            {
                Response(id, ZoqFotInfo);
                infoLeft = true;
            }
        }
        Response("enough_info", ZoqFotHome);

        if (!infoLeft)
            DisablePhrase("want_specific_info");
    }

    private void ZoqFotHome(int r)
    {
        int numVisits;

        if (PlayerSaid(r, "whats_up_homeworld"))
        {
            numVisits = GetGameState("ZOQFOT_INFO");
            switch (numVisits++)
            {
                case 0:
                    Say("GENERAL_INFO_1", 4);
                    break;
                case 1:
                    Say("GENERAL_INFO_2", 8);
                    break;
                case 2:
                    Say("GENERAL_INFO_3", 6);
                    break;
                case 3:
                    Say("GENERAL_INFO_4", 12);
                    --numVisits;
                    break;
            }
            SetGameState("ZOQFOT_INFO", numVisits);

            DisablePhrase("whats_up_homeworld");
        }
        else if (PlayerSaid(r, "any_war_news"))
        {
            numVisits = GetGameState("UTWIG_SUPOX_MISSION");
            int knowMask = GetGameState("ZOQFOT_KNOW_MASK");
            if ((knowMask & KohrAhKill) == 0 && GetGameState("KOHR_AH_FRENZY") != 0)
            {
                Say("KOHRAH_FRENZY", 12);

                knowMask = KnowAll;
            }
            else if ((knowMask & UtwigBuyTime) == 0 && numVisits > 0 && numVisits < 5)
            {
                Say("UTWIG_DELAY", 14);

                knowMask |= UtwigBuyTime;
            }
            else
            {
                // SIZE i: the years to the Kohr-Ah's victory (a year later
                // while the Utwig and Supox are on their mission or back), 0 in
                // its last year from March.
                int i = StartYear + YearsToKohrAhVictory;
                if (numVisits != 0)
                    ++i;
                if ((i -= T.Clock.Year) == 1 && T.Clock.Month > 2)
                    i = 0;
                if ((knowMask & UrquanLose) == 0 && i <= 0)
                {
                    Say("URQUAN_NEARLY_GONE", 6);

                    knowMask |= KohrAhWin | UrquanLose;
                }
                else if ((knowMask & KohrAhWin) == 0 && i == 1)
                {
                    Say("KOHRAH_WINNING", 10);

                    knowMask |= KohrAhWin;
                }
                else
                    Say("NO_WAR_NEWS", 2);
            }
            SetGameState("ZOQFOT_KNOW_MASK", knowMask);

            DisablePhrase("any_war_news");
        }
        else if (PlayerSaid(r, "i_want_alliance"))
        {
            Say("GOOD", 10);

            T.SetRaceAllied(ZoqFotPikShip, true);
            // The distress event: set aside until stage 3c runs the calendar.
            T.AddEvent(relative: true, 3, 0, 0, "ZOQFOT_DISTRESS_EVENT");
        }
        else if (PlayerSaid(r, "enough_info"))
        {
            T.NPCPhrase("OK_ENOUGH_INFO", SelectAlienZOQ);
            ZFPTalkSegue(Talk.WaitAll);
        }

        if (PhraseEnabled("whats_up_homeworld"))
            Response("whats_up_homeworld", ZoqFotHome);
        if (PhraseEnabled("any_war_news"))
            Response("any_war_news", ZoqFotHome);
        if (T.CheckAlliance(ZoqFotPikShip) != Story.Allied.Good)
            Response("i_want_alliance", ZoqFotHome);
        else if (PhraseEnabled("want_specific_info"))
            Response("want_specific_info", ZoqFotInfo);
        Response("bye_homeworld", ExitConversation);
    }

    protected internal override void Intro()
    {
        int numVisits;

        if (T.Activity == TalkActivity.WonLastBattle)
        {
            Say("OUT_TAKES", 14);
            T.SetSegue(Segue.Peace);
            return;
        }

        if (GetGameState("ZOQFOT_HOSTILE") != 0)
        {
            numVisits = GetGameState("ZOQFOT_HOME_VISITS");
            switch (numVisits++)
            {
                case 0:
                    Say("HOSTILE_HELLO_1", 2);
                    break;
                case 1:
                    Say("HOSTILE_HELLO_2", 6);
                    break;
                case 2:
                    Say("HOSTILE_HELLO_3", 2);
                    break;
                case 3:
                    Say("HOSTILE_HELLO_4", 2);
                    --numVisits;
                    break;
            }
            SetGameState("ZOQFOT_HOME_VISITS", numVisits);

            T.SetSegue(Segue.Hostile);
        }
        else if (GetGameState("MET_ZOQFOT") == 0)
        {
            SetGameState("MET_ZOQFOT", 1);

            Lines("WE_ARE", 8);
            if ((GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0)
                Lines("INIT_HOME_HELLO", 4);
            else
                Lines("SCOUT_HELLO", 4);
            ZFPTalkSegue(Talk.WaitAll);

            AquaintZoqFot(0);
        }
        else
        {
            if (GetGameState("ZOQFOT_DISTRESS") != 0)
            {
                Say("THANKS_FOR_RESCUE", 12);

                SetGameState("ZOQFOT_DISTRESS", 0);
                T.AddEscortShips(ZoqFotPikShip, MaxZfpShips);
            }
            else
            {
                numVisits = GetGameState("ZOQFOT_HOME_VISITS");
                if (T.CheckAlliance(ZoqFotPikShip) != Story.Allied.Good)
                {
                    switch (numVisits++)
                    {
                        case 0:
                            Lines("NEUTRAL_HOME_HELLO_1", 4);
                            break;
                        case 1:
                            Lines("NEUTRAL_HOME_HELLO_2", 4);
                            --numVisits;
                            break;
                    }
                    ZFPTalkSegue(Talk.WaitAll);
                }
                else
                {
                    switch (numVisits++)
                    {
                        case 0:
                            Say("ALLIED_HOME_HELLO_1", 4);
                            break;
                        case 1:
                            Say("ALLIED_HOME_HELLO_2", 8);
                            break;
                        case 2:
                            Say("ALLIED_HOME_HELLO_3", 2);
                            break;
                        case 3:
                            Say("ALLIED_HOME_HELLO_4", 2);
                            --numVisits;
                            break;
                    }
                }
                SetGameState("ZOQFOT_HOME_VISITS", numVisits);
            }

            ZoqFotHome(0);
        }
    }

    protected internal override void Init()
    {
        // AlienTextWidth: in the description already ((SIS_TEXT_WIDTH >> 1) - TEXT_X_OFFS).
        if (T.CheckAlliance(ZoqFotPikShip) == Story.Allied.Good || T.Activity == TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
