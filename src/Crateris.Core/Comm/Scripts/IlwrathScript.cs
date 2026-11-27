// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/ilwrath/ilwrathc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Ilwrath's talk (ilwrathc.c), whole: the ship that answers the
/// Ur-Quan probe's call at Earth (PROBE_ILWRATH_ENCOUNTER, the opening's,
/// whose replies all end in battle), the Ilwrath met in space (by the
/// visits and the news asked before, and once deceived into fighting the
/// Thraddash), at their homeworld (GLOBAL_FLAGS_AND_DATA bit 7, set by
/// genilw.c's orbit: the gods and the Ilwrath asked about), the Chmmr's
/// (bit 6: Procyon's, genchmmr.c's orbit with the Sun Device aboard, wired
/// since stage 3b-2 item 1 and reached by --start-state until 3c), and
/// stage 3c's, with the device: a HyperWave Broadcaster's to a ship
/// (bit 5) or to the homeworld (bit 4, the "gods" ordering the Ilwrath
/// against the Thraddash: ADVANCE_ILWRATH_MISSION, run at once, since stage
/// 3c item 3). Every path is
/// checked against UQM's own script (tests/golden/talks-ilwrath.json).
/// </summary>
public sealed class IlwrathScript : CommScript
{
    public IlwrathScript(ConversationScript phrases) : base(phrases)
    {
    }

    private void CombatIsInevitable(int r)
    {
        T.SetSegue(Segue.Hostile);

        if (PlayerSaid(r, "you_are_weak"))
            NPCPhrase("STRENGTH_NOT_ALL");
        else if (PlayerSaid(r, "slay_by_thousands"))
            NPCPhrase("NO_SLAY_BY_THOUSANDS");
        else if (PlayerSaid(r, "ease_up"))
            NPCPhrase("NO_EASE_UP");
        else if (PlayerSaid(r, "bye_space"))
            NPCPhrase("GOODBYE_AND_DIE_SPACE");
        else if (PlayerSaid(r, "bye_homeworld"))
            NPCPhrase("GOODBYE_AND_DIE_HOMEWORLD");
        else if (PlayerSaid(r, "want_peace"))
            NPCPhrase("NO_PEACE");
        else if (PlayerSaid(r, "want_alliance"))
            NPCPhrase("NO_ALLIANCE");
        else if (PlayerSaid(r, "but_evil_is_defined"))
            NPCPhrase("DONT_CONFUSE_US");
        else if (PlayerSaid(r, "bye_gods"))
        {
            NPCPhrase("GOODBYE_GODS");

            T.SetSegue(Segue.Peace);
        }
        if (PlayerSaid(r, "whats_up"))
        {
            NPCPhrase("GENERAL_INFO");
            Response("bye", CombatIsInevitable);
        }
        else if (PlayerSaid(r, "whats_up_space_1") || PlayerSaid(r, "whats_up_space_2")
            || PlayerSaid(r, "whats_up_space_3") || PlayerSaid(r, "whats_up_space_4")
            || PlayerSaid(r, "whats_up_space_5"))
        {
            int numVisits = GetGameState("ILWRATH_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_SPACE_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_SPACE_2");
                    break;
                case 2:
                    NPCPhrase("GENERAL_INFO_SPACE_3");
                    break;
                case 3:
                    NPCPhrase("GENERAL_INFO_SPACE_4");
                    break;
                case 4:
                    NPCPhrase("GENERAL_INFO_SPACE_5");
                    --numVisits;
                    break;
            }
            SetGameState("ILWRATH_INFO", numVisits);
        }
        else
        {
            if (PlayerSaid(r, "bye"))
                NPCPhrase("GOODBYE_AND_DIE");
            else if (PlayerSaid(r, "where_you_come_from"))
                NPCPhrase("CAME_FROM");
            if (PlayerSaid(r, "it_will_be_a_pleasure"))
                NPCPhrase("WHO_BLASTS_WHO");
            if (PlayerSaid(r, "surrender"))
                NPCPhrase("NO_SURRENDER");
            if (PlayerSaid(r, "be_reasonable"))
                NPCPhrase("NOT_REASONABLE");
        }
    }

    private void IlwrathGods(int r)
    {
        bool godsLeft = false;
        if (PlayerSaid(r, "want_info_on_gods"))
            NPCPhrase("SO_MUCH_TO_KNOW");
        else if (PlayerSaid(r, "when_start_worship"))
        {
            NPCPhrase("LONG_AGO");

            DisablePhrase("when_start_worship");
        }
        else if (PlayerSaid(r, "any_good_gods"))
        {
            NPCPhrase("KILLED_GOOD_GODS");

            DisablePhrase("any_good_gods");
        }
        else if (PlayerSaid(r, "how_talk_with_gods"))
        {
            NPCPhrase("CHANNEL_44");

            DisablePhrase("how_talk_with_gods");
        }
        else if (PlayerSaid(r, "why_44"))
        {
            NPCPhrase("BECAUSE_44");

            DisablePhrase("why_44");
        }

        if (PhraseEnabled("when_start_worship"))
        {
            Response("when_start_worship", IlwrathGods);
            godsLeft = true;
        }
        if (PhraseEnabled("any_good_gods"))
        {
            Response("any_good_gods", IlwrathGods);
            godsLeft = true;
        }
        if (PhraseEnabled("how_talk_with_gods"))
        {
            Response("how_talk_with_gods", IlwrathGods);
            godsLeft = true;
        }
        else if (PhraseEnabled("why_44"))
        {
            Response("why_44", IlwrathGods);
            godsLeft = true;
        }
        Response("enough_gods", IlwrathHome);

        if (!godsLeft)
            DisablePhrase("want_info_on_gods");
    }

    private void IlwrathInfo(int r)
    {
        bool infoLeft = false;
        if (PlayerSaid(r, "want_info_on_ilwrath"))
            NPCPhrase("WHAT_ABOUT_ILWRATH");
        else if (PlayerSaid(r, "what_about_physio"))
        {
            NPCPhrase("ABOUT_PHYSIO");

            DisablePhrase("what_about_physio");
        }
        else if (PlayerSaid(r, "what_about_history"))
        {
            NPCPhrase("ABOUT_HISTORY");

            DisablePhrase("what_about_history");
        }
        else if (PlayerSaid(r, "what_about_culture"))
        {
            NPCPhrase("ABOUT_CULTURE");

            DisablePhrase("what_about_culture");
        }
        else if (PlayerSaid(r, "what_about_urquan"))
        {
            NPCPhrase("URQUAN_TOO_NICE");

            DisablePhrase("what_about_urquan");
        }
        else if (PlayerSaid(r, "are_you_evil"))
        {
            NPCPhrase("OF_COURSE_WERE_EVIL");

            DisablePhrase("are_you_evil");
        }

        if (PhraseEnabled("what_about_physio"))
        {
            Response("what_about_physio", IlwrathInfo);
            infoLeft = true;
        }
        if (PhraseEnabled("what_about_history"))
        {
            Response("what_about_history", IlwrathInfo);
            infoLeft = true;
        }
        if (PhraseEnabled("what_about_culture"))
        {
            Response("what_about_culture", IlwrathInfo);
            infoLeft = true;
        }
        if (PhraseEnabled("what_about_urquan"))
        {
            Response("what_about_urquan", IlwrathInfo);
            infoLeft = true;
        }
        if (PhraseEnabled("are_you_evil"))
        {
            Response("are_you_evil", IlwrathInfo);
            infoLeft = true;
        }
        else
        {
            Response("but_evil_is_defined", CombatIsInevitable);
            infoLeft = true;
        }
        Response("enough_ilwrath", IlwrathHome);

        if (!infoLeft)
            DisablePhrase("want_info_on_ilwrath");
    }

    private void IlwrathHome(int r)
    {
        if (r == 0)
        {
            int numVisits = GetGameState("ILWRATH_HOME_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("INIT_HOME_HELLO");
                    break;
                case 1:
                    NPCPhrase("SUBSEQUENT_HOME_HELLO");
                    --numVisits;
                    break;
            }
            SetGameState("ILWRATH_HOME_VISITS", numVisits);
        }
        else if (PlayerSaid(r, "enough_gods"))
            NPCPhrase("OK_ENOUGH_GODS");
        else if (PlayerSaid(r, "enough_ilwrath"))
            NPCPhrase("OK_ENOUGH_ILWRATH");

        if (PhraseEnabled("want_info_on_gods"))
            Response("want_info_on_gods", IlwrathGods);
        if (PhraseEnabled("want_info_on_ilwrath"))
            Response("want_info_on_ilwrath", IlwrathInfo);
        Response("want_peace", CombatIsInevitable);
        Response("want_alliance", CombatIsInevitable);
        Response("bye_homeworld", CombatIsInevitable);
    }

    private void GodsOrder(int r)
    {
        bool ordersLeft = false;
        if (PlayerSaid(r, "other_divine_orders"))
            NPCPhrase("WHAT_ORDERS");
        else if (PlayerSaid(r, "say_warship"))
        {
            NPCPhrase("OK_WARSHIP");

            DisablePhrase("say_warship");
        }
        else if (PlayerSaid(r, "say_dwe"))
        {
            NPCPhrase("OK_DWE");

            DisablePhrase("say_dwe");
        }
        else if (PlayerSaid(r, "say_youboo"))
        {
            NPCPhrase("OK_YOUBOO");

            DisablePhrase("say_youboo");
        }
        else if (PlayerSaid(r, "say_dillrat"))
        {
            NPCPhrase("OK_DILRAT");

            DisablePhrase("say_dillrat");
        }

        if (PhraseEnabled("say_warship"))
        {
            Response("say_warship", GodsOrder);
            ordersLeft = true;
        }
        if (PhraseEnabled("say_dwe"))
        {
            Response("say_dwe", GodsOrder);
            ordersLeft = true;
        }
        if (PhraseEnabled("say_youboo"))
        {
            Response("say_youboo", GodsOrder);
            ordersLeft = true;
        }
        if (PhraseEnabled("say_dillrat"))
        {
            Response("say_dillrat", GodsOrder);
            ordersLeft = true;
        }
        Response("enough_orders", GodsSpeak);

        if (!ordersLeft)
            DisablePhrase("other_divine_orders");
    }

    private void GodsSpeak(int r)
    {
        int numVisits;

        if (r == 0)
        {
            if (GetGameState("ILWRATH_FIGHT_THRADDASH") != 0)
                NPCPhrase("GLORIOUS_WORSHIP");
            else if (GetGameState("ILWRATH_DECEIVED") != 0)
                NPCPhrase("ON_WAY");
            else
            {
                numVisits = GetGameState("ILWRATH_GODS_SPOKEN");
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("ILWRATH_BELIEVE");
                        break;
                    case 1:
                        NPCPhrase("GODS_RETURN_1");
                        break;
                    case 2:
                        NPCPhrase("GODS_RETURN_2");
                        break;
                    case 3:
                        NPCPhrase("GODS_RETURN_3");
                        --numVisits;
                        break;
                }
                SetGameState("ILWRATH_GODS_SPOKEN", numVisits);
            }
        }
        else if (PlayerSaid(r, "go_kill_thraddash"))
        {
            NPCPhrase("OK_KILL_THRADDASH");

            SetGameState("ILWRATH_DECEIVED", 1);
            T.AddEvent(relative: true, 0, 0, 0, "ADVANCE_ILWRATH_MISSION");
        }
        else if (PlayerSaid(r, "worship_us"))
        {
            numVisits = GetGameState("ILWRATH_WORSHIP");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("WE_WORSHIP_1");
                    break;
                case 1:
                    NPCPhrase("WE_WORSHIP_2");
                    break;
                case 2:
                    NPCPhrase("WE_WORSHIP_3");
                    --numVisits;
                    break;
            }
            SetGameState("ILWRATH_WORSHIP", numVisits);

            DisablePhrase("worship_us");
        }
        else if (PlayerSaid(r, "enough_orders"))
            NPCPhrase("NEVER_ENOUGH");

        if (GetGameState("ILWRATH_DECEIVED") == 0)
            Response("go_kill_thraddash", GodsSpeak);
        if (PhraseEnabled("worship_us"))
            Response("worship_us", GodsSpeak);
        if (PhraseEnabled("other_divine_orders"))
            Response("other_divine_orders", GodsOrder);
        Response("bye_gods", CombatIsInevitable);
    }

    private void IlwrathSpace(int r)
    {
        int numVisits;

        if (r == 0)
        {
            numVisits = GetGameState("ILWRATH_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("INIT_HELLO_SPACE");
                    break;
                case 1:
                    NPCPhrase("SUBSEQUENT_HELLO_SPACE_1");
                    break;
                case 2:
                    NPCPhrase("SUBSEQUENT_HELLO_SPACE_2");
                    break;
                case 3:
                    NPCPhrase("SUBSEQUENT_HELLO_SPACE_3");
                    break;
                case 4:
                    NPCPhrase("SUBSEQUENT_HELLO_SPACE_4");
                    --numVisits;
                    break;
            }
            SetGameState("ILWRATH_VISITS", numVisits);
        }

        switch (GetGameState("ILWRATH_INFO"))
        {
            case 0:
                Response("whats_up_space_1", CombatIsInevitable);
                break;
            case 1:
                Response("whats_up_space_2", CombatIsInevitable);
                break;
            case 2:
                Response("whats_up_space_3", CombatIsInevitable);
                break;
            case 3:
                Response("whats_up_space_4", CombatIsInevitable);
                break;
            case 4:
                Response("whats_up_space_5", CombatIsInevitable);
                break;
        }
        Response("you_are_weak", CombatIsInevitable);
        Response("slay_by_thousands", CombatIsInevitable);
        Response("ease_up", CombatIsInevitable);
        Response("bye_space", CombatIsInevitable);
    }

    protected internal override void Intro()
    {
        int flags = GetGameState("GLOBAL_FLAGS_AND_DATA");
        if (GetGameState("PROBE_ILWRATH_ENCOUNTER") != 0)
        {
            NPCPhrase("SEND_MESSAGE");

            Response("where_you_come_from", CombatIsInevitable);
            Response("it_will_be_a_pleasure", CombatIsInevitable);
            Response("surrender", CombatIsInevitable);
            Response("be_reasonable", CombatIsInevitable);
        }
        else if ((flags & (1 << 7)) != 0)
            IlwrathHome(0);
        else if ((flags & (1 << 6)) != 0)
        {
            int numVisits = GetGameState("ILWRATH_CHMMR_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("INIT_CHMMR_HELLO");
                    break;
                case 1:
                    NPCPhrase("SUBSEQUENT_CHMMR_HELLO");
                    --numVisits;
                    break;
            }
            SetGameState("ILWRATH_CHMMR_VISITS", numVisits);

            Response("whats_up", CombatIsInevitable);
            Response("bye", CombatIsInevitable);
        }
        else if ((flags & (1 << 5)) != 0)
        {
            // Communicating with an Ilwrath ship using a HyperWave Broadcaster.
            if (GetGameState("ILWRATH_FIGHT_THRADDASH") != 0)
                NPCPhrase("BIG_FUN");
            else if (GetGameState("ILWRATH_DECEIVED") != 0)
                NPCPhrase("FAST_AS_CAN");
            else
                NPCPhrase("JUST_GRUNTS");

            T.SetSegue(Segue.Peace);
        }
        else if ((flags & (1 << 4)) != 0)
        {
            // Communicating with the Ilwrath homeworld using a HyperWave Broadcaster.
            GodsSpeak(0);
        }
        else
        {
            T.SetSegue(Segue.Peace);

            if (GetGameState("ILWRATH_FIGHT_THRADDASH") != 0)
                NPCPhrase("HAPPY_FIGHTING_THRADDASH");
            else if (GetGameState("ILWRATH_DECEIVED") != 0)
                NPCPhrase("ON_WAY_TO_THRADDASH");
            else
                IlwrathSpace(0);
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if (GetGameState("PROBE_ILWRATH_ENCOUNTER") != 0
            || (GetGameState("GLOBAL_FLAGS_AND_DATA") & ((1 << 4) | (1 << 5))) != 0
            || T.Activity == TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
