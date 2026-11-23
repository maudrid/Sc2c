// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/vux/vuxc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The VUX (vuxc.c), whole: met in space (VUX_VISITS) and at their
/// homeworld (GLOBAL_FLAGS_AND_DATA bit 7, VUX_HOME_VISITS), where every
/// talk ends in a fight: their reasons asked (VUX_STACK_1 to the
/// apologies, which are never accepted, and "the truth"), the threats
/// (VUX_STACK_2), their news (VUX_INFO), the friendship asked once the
/// apologies are spent (VUX_STACK_3) and the farewells (VUX_STACK_4).
/// Admiral ZEX at the Maidens' world (bit 6, genvux.c) is at peace: his
/// menagerie, why he doesn't attack, the Precursor device he lied about
/// (ZEX_STACK_1 to 3), and with the beast aboard (VUX_BEAST_ON_SHIP, 3c's
/// pickup: reached by --start-devices) and his wish known
/// (KNOW_ZEX_WANTS_MONSTER), the beast given: it kills him (ZEX_IS_DEAD,
/// his colour map faded to its second). Every path is checked against
/// UQM's own script (tests/golden/talks-vux.json).
/// </summary>
public sealed class VuxScript : CommScript
{
    public VuxScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private void CombatIsInevitable(int r)
    {
        int numVisits;

        T.SetSegue(Segue.Hostile);

        if (PlayerSaid(r, "ok_take_beast"))
        {
            NPCPhrase("FOOL_AIEE0");
            NPCPhrase("FOOL_AIEE1");

            T.AlienTalkSegue(1);
            T.XFormColorMap(1, Talk.OneSecond / 4);
            T.AlienTalkSegue(Talk.WaitAll);

            SetGameState("VUX_BEAST_ON_SHIP", 0);
            SetGameState("ZEX_IS_DEAD", 1);
            T.SetSegue(Segue.Peace);
        }
        else if (PlayerSaid(r, "try_any_way"))
        {
            NPCPhrase("NOPE");

            SetGameState("VUX_STACK_1", 4);
        }
        else if (PlayerSaid(r, "kill_you_squids_1") || PlayerSaid(r, "kill_you_squids_2")
                 || PlayerSaid(r, "kill_you_squids_3") || PlayerSaid(r, "kill_you_squids_4"))
        {
            NPCPhrase("WE_FIGHT");

            numVisits = (byte)(GetGameState("VUX_STACK_2") + 1);
            if (numVisits <= 3)
                SetGameState("VUX_STACK_2", numVisits);
        }
        else if (PlayerSaid(r, "cant_we_be_friends_1") || PlayerSaid(r, "cant_we_be_friends_2")
                 || PlayerSaid(r, "cant_we_be_friends_3") || PlayerSaid(r, "cant_we_be_friends_4"))
        {
            numVisits = GetGameState("VUX_STACK_3");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("NEVER_UGLY_HUMANS_1");
                    break;
                case 1:
                    NPCPhrase("NEVER_UGLY_HUMANS_2");
                    break;
                case 2:
                    NPCPhrase("NEVER_UGLY_HUMANS_3");
                    break;
                case 3:
                    NPCPhrase("NEVER_UGLY_HUMANS_4");
                    --numVisits;
                    break;
            }
            SetGameState("VUX_STACK_3", numVisits);
        }
        else if (PlayerSaid(r, "bye_hostile_space"))
        {
            numVisits = GetGameState("VUX_STACK_4");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GOODBYE_AND_DIE_HOSTILE_SPACE_1");
                    break;
                case 1:
                    NPCPhrase("GOODBYE_AND_DIE_HOSTILE_SPACE_2");
                    break;
                case 2:
                    NPCPhrase("GOODBYE_AND_DIE_HOSTILE_SPACE_3");
                    break;
                case 3:
                    NPCPhrase("GOODBYE_AND_DIE_HOSTILE_SPACE_4");
                    --numVisits;
                    break;
            }
            SetGameState("VUX_STACK_4", numVisits);
        }
        else if (PlayerSaid(r, "bye_zex"))
        {
            NPCPhrase("GOODBYE_ZEX");

            T.SetSegue(Segue.Peace);
        }
        else
        {
            // The apologies (apology_1 to 10, offered while VUX_STACK_1 is 4
            // to 13), each one's answer: the count stops at 14, past TRUTH.
            numVisits = GetGameState("VUX_STACK_1");
            switch (numVisits++)
            {
                case 4:
                    NPCPhrase("NOT_ACCEPTED_1");
                    break;
                case 5:
                    NPCPhrase("NOT_ACCEPTED_2");
                    break;
                case 6:
                    NPCPhrase("NOT_ACCEPTED_3");
                    break;
                case 7:
                    NPCPhrase("NOT_ACCEPTED_4");
                    break;
                case 8:
                    NPCPhrase("NOT_ACCEPTED_5");
                    break;
                case 9:
                    NPCPhrase("NOT_ACCEPTED_6");
                    break;
                case 10:
                    NPCPhrase("NOT_ACCEPTED_7");
                    break;
                case 11:
                    NPCPhrase("NOT_ACCEPTED_8");
                    break;
                case 12:
                    NPCPhrase("NOT_ACCEPTED_9");
                    break;
                case 13:
                    NPCPhrase("TRUTH");
                    break;
            }
            SetGameState("VUX_STACK_1", numVisits);
        }
    }

    private void Menagerie(int r)
    {
        if (PlayerSaid(r, "i_have_beast") || PlayerSaid(r, "why_trust_1") || PlayerSaid(r, "why_trust_2")
            || PlayerSaid(r, "why_trust_3"))
        {
            if (PlayerSaid(r, "i_have_beast"))
                NPCPhrase("GIVE_BEAST");
            else if (PlayerSaid(r, "why_trust_1"))
            {
                NPCPhrase("TRUST_1");

                DisablePhrase("why_trust_1");
            }
            else if (PlayerSaid(r, "why_trust_2"))
            {
                NPCPhrase("TRUST_2");

                DisablePhrase("why_trust_2");
            }
            else if (PlayerSaid(r, "why_trust_3"))
            {
                NPCPhrase("TRUST_3");

                DisablePhrase("why_trust_3");
            }

            if (PhraseEnabled("why_trust_1"))
                Response("why_trust_1", Menagerie);
            else if (PhraseEnabled("why_trust_2"))
                Response("why_trust_2", Menagerie);
            else if (PhraseEnabled("why_trust_3"))
                Response("why_trust_3", Menagerie);
            Response("ok_take_beast", CombatIsInevitable);
        }
        else if (PlayerSaid(r, "kill_you"))
        {
            NPCPhrase("FIGHT_AGAIN");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "regardless"))
        {
            NPCPhrase("THEN_FIGHT");

            T.SetSegue(Segue.Hostile);
            SetGameState("ZEX_STACK_3", 2);
            SetGameState("ZEX_VISITS", 0);
        }
        else
        {
            int lastStack = 0;
            var pStr = new string?[3];
            if (r == 0)
            {
                int numVisits = GetGameState("ZEX_VISITS");
                if (GetGameState("ZEX_STACK_3") >= 2)
                {
                    switch (numVisits++)
                    {
                        case 0:
                            NPCPhrase("FIGHT_OR_TRADE_1");
                            break;
                        case 1:
                            NPCPhrase("FIGHT_OR_TRADE_2");
                            --numVisits;
                            break;
                    }
                }
                else
                {
                    switch (numVisits++)
                    {
                        case 0:
                            NPCPhrase("ZEX_HELLO_1");
                            break;
                        case 1:
                            NPCPhrase("ZEX_HELLO_2");
                            break;
                        case 2:
                            NPCPhrase("ZEX_HELLO_3");
                            break;
                        case 3:
                            NPCPhrase("ZEX_HELLO_4");
                            --numVisits;
                            break;
                    }
                }
                SetGameState("ZEX_VISITS", numVisits);
            }
            else if (PlayerSaid(r, "what_you_do_here"))
            {
                NPCPhrase("MY_MENAGERIE");

                SetGameState("ZEX_STACK_1", 1);
            }
            else if (PlayerSaid(r, "what_about_menagerie"))
            {
                NPCPhrase("NEED_NEW_CREATURE");

                SetGameState("ZEX_STACK_1", 2);
            }
            else if (PlayerSaid(r, "what_about_creature"))
            {
                NPCPhrase("ABOUT_CREATURE");

                SetGameState("KNOW_ZEX_WANTS_MONSTER", 1);
                SetGameState("ZEX_STACK_1", 3);

                // vuxc.c sets R to about_creature_again here, which nothing reads after.
                DisablePhrase("what_about_creature");
            }
            else if (PlayerSaid(r, "about_creature_again"))
            {
                NPCPhrase("CREATURE_AGAIN");

                DisablePhrase("about_creature_again");
            }
            else if (PlayerSaid(r, "why_dont_you_attack"))
            {
                NPCPhrase("LIKE_YOU");

                lastStack = 1;
                SetGameState("ZEX_STACK_2", 1);
            }
            else if (PlayerSaid(r, "why_like_me"))
            {
                NPCPhrase("LIKE_BECAUSE");

                lastStack = 1;
                SetGameState("ZEX_STACK_2", 2);
            }
            else if (PlayerSaid(r, "are_you_a_pervert"))
            {
                NPCPhrase("CALL_ME_WHAT_YOU_WISH");

                SetGameState("ZEX_STACK_2", 3);
            }
            else if (PlayerSaid(r, "take_by_force"))
            {
                NPCPhrase("PRECURSOR_DEVICE");

                lastStack = 2;
                SetGameState("ZEX_STACK_3", 1);
            }
            else if (PlayerSaid(r, "you_lied"))
            {
                NPCPhrase("YUP_LIED");

                lastStack = 2;
                SetGameState("ZEX_STACK_3", 3);
            }

            if (GetGameState("KNOW_ZEX_WANTS_MONSTER") != 0 && GetGameState("VUX_BEAST_ON_SHIP") != 0)
                pStr[0] = "i_have_beast";
            else
            {
                switch (GetGameState("ZEX_STACK_1"))
                {
                    case 0:
                        pStr[0] = "what_you_do_here";
                        break;
                    case 1:
                        pStr[0] = "what_about_menagerie";
                        break;
                    case 2:
                        pStr[0] = "what_about_creature";
                        break;
                    case 3:
                        if (PhraseEnabled("about_creature_again"))
                            pStr[0] = "about_creature_again";
                        break;
                }
            }
            switch (GetGameState("ZEX_STACK_2"))
            {
                case 0:
                    pStr[1] = "why_dont_you_attack";
                    break;
                case 1:
                    pStr[1] = "why_like_me";
                    break;
                case 2:
                    pStr[1] = "are_you_a_pervert";
                    break;
            }
            switch (GetGameState("ZEX_STACK_3"))
            {
                case 0:
                    pStr[2] = "take_by_force";
                    break;
                case 1:
                    pStr[2] = "regardless";
                    break;
                case 2:
                    pStr[2] = "you_lied";
                    break;
                case 3:
                    pStr[2] = "kill_you";
                    break;
            }

            if (pStr[lastStack] is { } last)
                Response(last, Menagerie);
            for (int i = 0; i < 3; ++i)
            {
                if (i != lastStack && pStr[i] is { } s)
                    Response(s, Menagerie);
            }
            Response("bye_zex", CombatIsInevitable);
        }
    }

    private void NormalVux(int r)
    {
        if (PlayerSaid(r, "why_so_mean"))
        {
            NPCPhrase("URQUAN_SLAVES");

            SetGameState("VUX_STACK_1", 1);
        }
        else if (PlayerSaid(r, "deeper_reason"))
        {
            NPCPhrase("OLD_INSULT");

            SetGameState("VUX_STACK_1", 2);
        }
        else if (PlayerSaid(r, "if_we_apologize"))
        {
            NPCPhrase("PROBABLY_NOT");

            SetGameState("VUX_STACK_1", 3);
        }
        else if (PlayerSaid(r, "whats_up_hostile"))
        {
            int numVisits = GetGameState("VUX_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_HOSTILE_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_HOSTILE_2");
                    break;
                case 2:
                    NPCPhrase("GENERAL_INFO_HOSTILE_3");
                    break;
                case 3:
                    NPCPhrase("GENERAL_INFO_HOSTILE_4");
                    --numVisits;
                    break;
            }
            SetGameState("VUX_INFO", numVisits);

            DisablePhrase("whats_up_hostile");
        }

        switch (GetGameState("VUX_STACK_1"))
        {
            case 0:
                Response("why_so_mean", NormalVux);
                break;
            case 1:
                Response("deeper_reason", NormalVux);
                break;
            case 2:
                Response("if_we_apologize", NormalVux);
                break;
            case 3:
                Response("try_any_way", CombatIsInevitable);
                break;
            case 4:
                Response("apology_1", CombatIsInevitable);
                break;
            case 5:
                Response("apology_2", CombatIsInevitable);
                break;
            case 6:
                Response("apology_3", CombatIsInevitable);
                break;
            case 7:
                Response("apology_4", CombatIsInevitable);
                break;
            case 8:
                Response("apology_5", CombatIsInevitable);
                break;
            case 9:
                Response("apology_6", CombatIsInevitable);
                break;
            case 10:
                Response("apology_7", CombatIsInevitable);
                break;
            case 11:
                Response("apology_8", CombatIsInevitable);
                break;
            case 12:
                Response("apology_9", CombatIsInevitable);
                break;
            case 13:
                Response("apology_10", CombatIsInevitable);
                break;
        }

        switch (GetGameState("VUX_STACK_2"))
        {
            case 0:
                Response("kill_you_squids_1", CombatIsInevitable);
                break;
            case 1:
                Response("kill_you_squids_2", CombatIsInevitable);
                break;
            case 2:
                Response("kill_you_squids_3", CombatIsInevitable);
                break;
            case 3:
                Response("kill_you_squids_4", CombatIsInevitable);
                break;
        }

        if (PhraseEnabled("whats_up_hostile"))
            Response("whats_up_hostile", NormalVux);

        if (GetGameState("VUX_STACK_1") > 13)
        {
            switch (GetGameState("VUX_STACK_3"))
            {
                case 0:
                    Response("cant_we_be_friends_1", CombatIsInevitable);
                    break;
                case 1:
                    Response("cant_we_be_friends_2", CombatIsInevitable);
                    break;
                case 2:
                    Response("cant_we_be_friends_3", CombatIsInevitable);
                    break;
                case 3:
                    Response("cant_we_be_friends_4", CombatIsInevitable);
                    break;
            }
        }

        Response("bye_hostile_space", CombatIsInevitable);
    }

    protected internal override void Intro()
    {
        if (T.Activity == TalkActivity.WonLastBattle)
        {
            NPCPhrase("OUT_TAKES");

            T.SetSegue(Segue.Peace);
            return;
        }

        if ((GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 6)) != 0)
            Menagerie(0);
        else
        {
            int numVisits;
            if ((GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0)
            {
                numVisits = GetGameState("VUX_HOME_VISITS");
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("HOMEWORLD_HELLO_1");
                        break;
                    case 1:
                        NPCPhrase("HOMEWORLD_HELLO_2");
                        break;
                    case 2:
                        NPCPhrase("HOMEWORLD_HELLO_3");
                        break;
                    case 3:
                        NPCPhrase("HOMEWORLD_HELLO_4");
                        --numVisits;
                        break;
                }
                SetGameState("VUX_HOME_VISITS", numVisits);
            }
            else
            {
                numVisits = GetGameState("VUX_VISITS");
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("SPACE_HELLO_1");
                        break;
                    case 1:
                        NPCPhrase("SPACE_HELLO_2");
                        break;
                    case 2:
                        NPCPhrase("SPACE_HELLO_3");
                        break;
                    case 3:
                        NPCPhrase("SPACE_HELLO_4");
                        --numVisits;
                        break;
                }
                SetGameState("VUX_VISITS", numVisits);
            }

            NormalVux(0);
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if ((GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 6)) != 0 || T.Activity == TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
