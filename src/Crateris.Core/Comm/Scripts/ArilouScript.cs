// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/arilou/arilouc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Arilou (arilouc.c), whole: met in hyperspace, and at their
/// homeworld in QuasiSpace, told apart by the answering player's
/// ARILOU_SPACE_SIDE (each ship's own: 2 or more in QuasiSpace, stage
/// 3c's; the talk's own field). In hyperspace hostile from init unless
/// they're the players' friends for good (ARILOU_MANNER 3), so the
/// encounter screen comes first: the first meeting's hello and their
/// small talk (MET_ARILOU), their greetings by visit, their news
/// (ARILOU_INFO), why they're here (ARILOU_STACK_5) and their hints
/// (ARILOU_HINTS: the shattered world, the Ur-Quan's and the Kohr-Ah's
/// story known), every way out in peace; after a fight they're angry
/// (post_arilou_enc: ARILOU_MANNER 1, their greetings counted afresh),
/// will apologise at their homeworld and won't fight ("let's fight" is
/// a goodbye); insulted at home they're the players' enemies for good
/// (ARILOU_MANNER 2), saying goodbye at once wherever met. At their
/// homeworld (QuasiSpace, stage 3c's, recorded): their questions and
/// stacks, the Talking Pet and the Umgah (ARILOU_CHECKED_UMGAH, the
/// ARILOU_UMGAH_CHECK event queued ten days on: set aside until 3c, the
/// stage 3b-2 plan's question 3), the wreck's part for the portal key
/// (KNOW_ARILOU_WANT_WRECK, PORTAL_KEY_ON_SHIP: the portal spawner
/// given), and after a talk there with ARILOU_HOME_VISITS at most 1 the
/// Umgah made the Pet's zombies again (post_arilou_enc:
/// UMGAH_ZOMBIE_BLOBBIES) and the Arilou friends for good. Every path is
/// checked against UQM's own script (tests/golden/talks-arilou.json).
/// </summary>
public sealed class ArilouScript : CommScript
{
    public ArilouScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    // ARILOU_SPACE_SIDE > 1: in QuasiSpace (the answering ship's own).
    private bool InQuasiSpace => GetGameState("ARILOU_SPACE_SIDE") > 1;

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Peace);

        if (PlayerSaid(r, "bye_angry_space"))
            NPCPhrase("GOODBYE_ANGRY_SPACE");
        else if (PlayerSaid(r, "bye_friendly_space"))
            NPCPhrase("GOODBYE_FRIENDLY_SPACE");
        else if (PlayerSaid(r, "bye_friendly_homeworld"))
            NPCPhrase("GOODBYE_FRDLY_HOMEWORLD");
        else if (PlayerSaid(r, "lets_fight"))
            NPCPhrase("NO_FIGHT");
        else if (PlayerSaid(r, "bug_eyed_fruitcakes"))
        {
            NPCPhrase("WE_NEVER_FRIENDS");

            SetGameState("ARILOU_MANNER", 2);
        }
        else if (PlayerSaid(r, "best_if_i_killed_you"))
        {
            NPCPhrase("WICKED_HUMAN");

            SetGameState("ARILOU_MANNER", 2);
        }
    }

    private void ArilouHome(int r)
    {
        int lastStack = 0;
        var pStr = new string?[4];
        if (PlayerSaid(r, "confused_by_hello"))
            NPCPhrase("CONFUSED_RESPONSE");
        else if (PlayerSaid(r, "happy_by_hello"))
            NPCPhrase("HAPPY_RESPONSE");
        else if (PlayerSaid(r, "miffed_by_hello"))
            NPCPhrase("MIFFED_RESPONSE");
        else if (PlayerSaid(r, "ok_lets_be_friends"))
            NPCPhrase("NO_ALLY_BUT_MUCH_GIVE");
        else if (PlayerSaid(r, "what_about_war"))
        {
            NPCPhrase("ABOUT_WAR");

            SetGameState("ARILOU_STACK_1", 1);
        }
        else if (PlayerSaid(r, "what_about_urquan"))
        {
            NPCPhrase("ABOUT_URQUAN");

            SetGameState("ARILOU_STACK_1", 2);
        }
        else if (PlayerSaid(r, "tell_arilou_about_tpet"))
        {
            NPCPhrase("BAD_NEWS_ABOUT_TPET");

            lastStack = 1;
            SetGameState("ARILOU_STACK_2", 1);
        }
        else if (PlayerSaid(r, "what_do_about_tpet"))
        {
            NPCPhrase("DANGEROUS_BUT_USEFUL");

            lastStack = 1;
            SetGameState("ARILOU_STACK_2", 2);
        }
        else if (PlayerSaid(r, "learned_about_umgah"))
        {
            if (GetGameState("ARILOU_CHECKED_UMGAH") != 2)
                NPCPhrase("NO_NEWS_YET");
            else
            {
                NPCPhrase("UMGAH_UNDER_COMPULSION");

                lastStack = 1;
            }

            DisablePhrase("learned_about_umgah");
        }
        else if (PlayerSaid(r, "umgah_acting_weird"))
        {
            NPCPhrase("WELL_GO_CHECK");

            SetGameState("ARILOU_CHECKED_UMGAH", 1);
            // Queued and set aside until stage 3c runs the calendar (the plan's question 3).
            T.AddEvent(relative: true, 0, 10, 0, "ARILOU_UMGAH_CHECK");
            DisablePhrase("umgah_acting_weird");
        }
        else if (PlayerSaid(r, "what_do_now"))
        {
            NPCPhrase("GO_FIND_OUT");

            SetGameState("ARILOU_CHECKED_UMGAH", 3);
        }
        else if (PlayerSaid(r, "what_did_on_earth"))
        {
            NPCPhrase("DID_THIS");

            lastStack = 2;
            SetGameState("ARILOU_STACK_3", 1);
        }
        else if (PlayerSaid(r, "why_did_this"))
        {
            NPCPhrase("IDF_PARASITES");

            lastStack = 2;
            SetGameState("ARILOU_STACK_3", 2);
        }
        else if (PlayerSaid(r, "tell_more"))
        {
            NPCPhrase("NOT_NOW");

            lastStack = 2;
            SetGameState("ARILOU_STACK_3", 3);
        }
        else if (PlayerSaid(r, "what_give_me"))
        {
            NPCPhrase("ABOUT_PORTAL");

            lastStack = 3;
            SetGameState("KNOW_ARILOU_WANT_WRECK", 1);

            // (arilouc.c sets R to about_portal_again here, then reads R no more.)
            DisablePhrase("what_give_me");
        }
        else if (PlayerSaid(r, "what_about_tpet"))
        {
            NPCPhrase("ABOUT_TPET");

            SetGameState("ARILOU_STACK_4", 1);
        }
        else if (PlayerSaid(r, "about_portal_again"))
        {
            NPCPhrase("PORTAL_AGAIN");

            DisablePhrase("about_portal_again");
        }
        else if (PlayerSaid(r, "got_it"))
        {
            if (GetGameState("ARILOU_HOME_VISITS") == 1)
                NPCPhrase("CLEVER_HUMAN");
            NPCPhrase("GIVE_PORTAL");

            SetGameState("PORTAL_KEY_ON_SHIP", 0);
            SetGameState("PORTAL_SPAWNER", 1);
            SetGameState("PORTAL_SPAWNER_ON_SHIP", 1);
        }
        // (got_tpet and OK_GOT_TPET are #ifdef NEVER in arilouc.c.)

        switch (GetGameState("ARILOU_STACK_1"))
        {
            case 0:
                pStr[0] = "what_about_war";
                break;
            case 1:
                pStr[0] = "what_about_urquan";
                break;
        }
        if (GetGameState("TALKING_PET") != 0)
        {
            // (#ifdef NEVER: got_tpet.)
        }
        else
        {
            if (GetGameState("TALKING_PET_VISITS") != 0)
            {
                switch (GetGameState("ARILOU_STACK_2"))
                {
                    case 0:
                        pStr[1] = "tell_arilou_about_tpet";
                        break;
                    case 1:
                        pStr[1] = "what_do_about_tpet";
                        break;
                }
            }
            else if (GetGameState("KNOW_UMGAH_ZOMBIES") != 0)
            {
                if (GetGameState("ARILOU_CHECKED_UMGAH") == 0)
                    pStr[1] = "umgah_acting_weird";
                else if (PhraseEnabled("learned_about_umgah") && PhraseEnabled("umgah_acting_weird"))
                    pStr[1] = "learned_about_umgah";
                else if (GetGameState("ARILOU_CHECKED_UMGAH") == 2)
                    pStr[1] = "what_do_now";
            }
        }
        switch (GetGameState("ARILOU_STACK_3"))
        {
            case 0:
                pStr[2] = "what_did_on_earth";
                break;
            case 1:
                pStr[2] = "why_did_this";
                break;
            case 2:
                pStr[2] = "tell_more";
                break;
        }
        if (GetGameState("KNOW_ARILOU_WANT_WRECK") == 0)
            pStr[3] = "what_give_me";
        else if (GetGameState("ARILOU_STACK_4") == 0)
            pStr[3] = "what_about_tpet";

        if (pStr[lastStack] is { } last)
            Response(last, ArilouHome);
        for (int i = 0; i < 4; ++i)
        {
            if (i != lastStack && pStr[i] is { } other)
                Response(other, ArilouHome);
        }

        if (GetGameState("KNOW_ARILOU_WANT_WRECK") != 0)
        {
            if (GetGameState("PORTAL_KEY_ON_SHIP") != 0)
                Response("got_it", ArilouHome);
            else if (PhraseEnabled("about_portal_again") && GetGameState("PORTAL_SPAWNER") == 0)
                Response("about_portal_again", ArilouHome);
        }
        if (GetGameState("ARILOU_MANNER") != 3)
            Response("best_if_i_killed_you", ExitConversation);
        Response("bye_friendly_homeworld", ExitConversation);
    }

    private void AngryHomeArilou(int r)
    {
        if (PlayerSaid(r, "invaders_from_mars"))
        {
            NPCPhrase("HAD_OUR_REASONS");

            DisablePhrase("invaders_from_mars");
        }
        else if (PlayerSaid(r, "why_should_i_trust"))
        {
            NPCPhrase("TRUST_BECAUSE");

            DisablePhrase("why_should_i_trust");
        }
        else if (PlayerSaid(r, "what_about_interference"))
        {
            NPCPhrase("INTERFERENCE_NECESSARY");

            DisablePhrase("what_about_interference");
        }
        else if (PlayerSaid(r, "i_just_like_to_leave"))
        {
            NPCPhrase("SORRY_NO_LEAVE");

            DisablePhrase("i_just_like_to_leave");
        }

        if (PhraseEnabled("invaders_from_mars"))
            Response("invaders_from_mars", AngryHomeArilou);
        else
            Response("bug_eyed_fruitcakes", ExitConversation);
        if (PhraseEnabled("why_should_i_trust"))
            Response("why_should_i_trust", AngryHomeArilou);
        else if (PhraseEnabled("what_about_interference"))
            Response("what_about_interference", AngryHomeArilou);
        Response("ok_lets_be_friends", ArilouHome);
        Response("i_just_like_to_leave", AngryHomeArilou);
    }

    private void AngrySpaceArilou(int r)
    {
        if (PlayerSaid(r, "im_sorry"))
        {
            NPCPhrase("APOLOGIZE_AT_HOMEWORLD");

            DisablePhrase("im_sorry");
        }

        Response("lets_fight", ExitConversation);
        if (PhraseEnabled("im_sorry"))
            Response("im_sorry", AngrySpaceArilou);
        Response("bye_angry_space", ExitConversation);
    }

    private void FriendlySpaceArilou(int r)
    {
        int numVisits;

        if (PlayerSaid(r, "confused_by_hello"))
            NPCPhrase("CONFUSED_RESPONSE");
        else if (PlayerSaid(r, "happy_by_hello"))
            NPCPhrase("HAPPY_RESPONSE");
        else if (PlayerSaid(r, "miffed_by_hello"))
            NPCPhrase("MIFFED_RESPONSE");
        else if (PlayerSaid(r, "whats_up_1") || PlayerSaid(r, "whats_up_2"))
        {
            numVisits = Greeting(GetGameState("ARILOU_INFO"),
                "GENERAL_INFO_1", "GENERAL_INFO_2", "GENERAL_INFO_3", "GENERAL_INFO_4");
            SetGameState("ARILOU_INFO", numVisits);

            DisablePhrase("whats_up_2");
        }
        else if (PlayerSaid(r, "why_you_here"))
        {
            NPCPhrase("LEARN_THINGS");

            SetGameState("ARILOU_STACK_5", 1);
        }
        else if (PlayerSaid(r, "what_things"))
        {
            NPCPhrase("THESE_THINGS");

            SetGameState("ARILOU_STACK_5", 2);
        }
        else if (PlayerSaid(r, "why_do_it"))
        {
            NPCPhrase("DO_IT_BECAUSE");

            SetGameState("ARILOU_STACK_5", 3);
        }
        else if (PlayerSaid(r, "give_me_info_1") || PlayerSaid(r, "give_me_info_2"))
        {
            numVisits = GetGameState("ARILOU_HINTS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("ARILOU_HINTS_1");
                    break;
                case 1:
                    NPCPhrase("ARILOU_HINTS_2");
                    if (GetGameState("KNOW_ABOUT_SHATTERED") < 2)
                        SetGameState("KNOW_ABOUT_SHATTERED", 2);
                    break;
                case 2:
                    NPCPhrase("ARILOU_HINTS_3");
                    SetGameState("KNOW_URQUAN_STORY", 1);
                    SetGameState("KNOW_KOHR_AH_STORY", 1);
                    break;
                case 3:
                    NPCPhrase("ARILOU_HINTS_4");
                    --numVisits;
                    break;
            }
            SetGameState("ARILOU_HINTS", (byte)numVisits);

            DisablePhrase("give_me_info_2");
        }

        switch (GetGameState("ARILOU_STACK_5"))
        {
            case 0:
                Response("why_you_here", FriendlySpaceArilou);
                break;
            case 1:
                Response("what_things", FriendlySpaceArilou);
                break;
            case 2:
                Response("why_do_it", FriendlySpaceArilou);
                break;
        }
        if (PhraseEnabled("whats_up_2"))
        {
            if (GetGameState("ARILOU_INFO") == 0)
                Response("whats_up_1", FriendlySpaceArilou);
            else
                Response("whats_up_2", FriendlySpaceArilou);
        }
        if (PhraseEnabled("give_me_info_2"))
        {
            if (GetGameState("ARILOU_HINTS") == 0)
                Response("give_me_info_1", FriendlySpaceArilou);
            else
                Response("give_me_info_2", FriendlySpaceArilou);
        }
        Response("bye_friendly_space", ExitConversation);
    }

    // The greetings by visit (a BYTE): each switch's last case keeps its
    // count there; past the cases nothing is said and the count goes on.
    private int Greeting(int numVisits, params string[] hellos)
    {
        if (numVisits < hellos.Length)
            NPCPhrase(hellos[numVisits]);
        if (numVisits == hellos.Length - 1)
            --numVisits;
        return (byte)(numVisits + 1);
    }

    protected internal override void Intro()
    {
        int numVisits;

        if (T.Activity == TalkActivity.WonLastBattle)
        {
            NPCPhrase("OUT_TAKES");

            T.SetSegue(Segue.Peace);
            return;
        }
        else if (GetGameState("MET_ARILOU") == 0)
        {
            Action<int> respFunc;

            if (!InQuasiSpace)
            {
                NPCPhrase("INIT_HELLO");
                respFunc = FriendlySpaceArilou;
            }
            else
            {
                NPCPhrase("FRDLY_HOMEWORLD_HELLO_1");
                respFunc = ArilouHome;
                SetGameState("ARILOU_HOME_VISITS", 1);
            }
            Response("confused_by_hello", respFunc);
            Response("happy_by_hello", respFunc);
            Response("miffed_by_hello", respFunc);
            SetGameState("MET_ARILOU", 1);
            return;
        }

        int manner = GetGameState("ARILOU_MANNER");
        if (manner == 2)
        {
            numVisits = Greeting(GetGameState("ARILOU_VISITS"),
                "HOSTILE_GOODBYE_1", "HOSTILE_GOODBYE_2", "HOSTILE_GOODBYE_3", "HOSTILE_GOODBYE_4");
            SetGameState("ARILOU_VISITS", numVisits);

            T.SetSegue(Segue.Peace);
        }
        else if (manner == 1)
        {
            if (InQuasiSpace)
            {
                NPCPhrase("INIT_ANGRY_HWLD_HELLO");
                SetGameState("ARILOU_HOME_VISITS", 1);

                AngryHomeArilou(0);
            }
            else
            {
                numVisits = Greeting(GetGameState("ARILOU_VISITS"), "ANGRY_SPACE_HELLO_1", "ANGRY_SPACE_HELLO_2");
                SetGameState("ARILOU_VISITS", numVisits);

                AngrySpaceArilou(0);
            }
        }
        else
        {
            if (!InQuasiSpace)
            {
                numVisits = Greeting(GetGameState("ARILOU_VISITS"), "FRIENDLY_SPACE_HELLO_1",
                    "FRIENDLY_SPACE_HELLO_2", "FRIENDLY_SPACE_HELLO_3", "FRIENDLY_SPACE_HELLO_4");
                SetGameState("ARILOU_VISITS", numVisits);

                FriendlySpaceArilou(0);
            }
            else
            {
                if (GetGameState("PORTAL_SPAWNER") == 0 && GetGameState("KNOW_ARILOU_WANT_WRECK") != 0)
                {
                    // Both cases say GOT_PART_YET_1 (arilouc.c's).
                    numVisits = Greeting(GetGameState("NO_PORTAL_VISITS"), "GOT_PART_YET_1", "GOT_PART_YET_1");
                    SetGameState("NO_PORTAL_VISITS", numVisits);
                }
                else
                {
                    numVisits = Greeting(GetGameState("ARILOU_HOME_VISITS"), "FRDLY_HOMEWORLD_HELLO_1",
                        "FRDLY_HOMEWORLD_HELLO_2", "FRDLY_HOMEWORLD_HELLO_3", "FRDLY_HOMEWORLD_HELLO_4");
                    SetGameState("ARILOU_HOME_VISITS", numVisits);
                }

                ArilouHome(0);
            }
        }
    }

    // post_arilou_enc: a hostile ending angers them (ARILOU_MANNER 1, their
    // greetings counted afresh) unless they're enemies for good; a talk at
    // their homeworld (QuasiSpace) while ARILOU_HOME_VISITS is at most 1
    // makes the Umgah the Talking Pet's zombies again and, unless they're
    // enemies for good, the Arilou friends for good (ARILOU_MANNER 3).
    protected internal override void PostEncounter()
    {
        int manner;
        if (T.Segue == Segue.Hostile && (manner = GetGameState("ARILOU_MANNER")) != 2)
        {
            SetGameState("ARILOU_MANNER", 1);
            if (manner != 1)
            {
                SetGameState("ARILOU_VISITS", 0);
                SetGameState("ARILOU_HOME_VISITS", 0);
            }
        }

        if (InQuasiSpace && GetGameState("ARILOU_HOME_VISITS") <= 1)
        {
            SetGameState("UMGAH_ZOMBIE_BLOBBIES", 1);
            SetGameState("UMGAH_VISITS", 0);
            SetGameState("UMGAH_HOME_VISITS", 0);

            if (GetGameState("ARILOU_MANNER") < 2)
                SetGameState("ARILOU_MANNER", 3);
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if (InQuasiSpace || GetGameState("ARILOU_MANNER") == 3 || T.Activity == TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
