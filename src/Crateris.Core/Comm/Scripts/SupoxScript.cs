// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/supox/supoxc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Supox (supoxc.c), whole: in space and at their homeworld
/// (GLOBAL_FLAGS_AND_DATA bit 7). No reply of theirs ends in a fight:
/// init leaves the talk at peace unless they've been insulted
/// (SUPOX_HOSTILE), and then the hostile greeting is the whole talk and
/// sets it at peace again, so only the encounter screen's Attack fights
/// them. The neutral talk (SUPOX_STACK1 and 2: the captain's, the ship's
/// and the alliance's names spliced into the replies they copy), the
/// Utwig asked about (SUPOX_WAR_NEWS: "anyone around here?" shows the
/// Utwig's sphere, StartSphereTracking, and is "what relation to the
/// Utwig?" once the Utwig have been met anywhere) down to the broken
/// Ultron handed over (ULTRON_CONDITION 1, the group's), kept politely
/// or with "now we eat you" (SUPOX_HOSTILE, their greetings counted
/// afresh); with the Ultron aboard the repairs asked about by its state
/// (SUPOX_ULTRON_HELP, once). The allies (made by the Utwig's script
/// when the mended Ultron is given): at home their news by the mission's
/// state (UTWIG_SUPOX_MISSION, SUPOX_WAR_NEWS) and 4 ships while the
/// mission hasn't started (to the host's fleet, whoever answers, while
/// it has room: EscortFeasibilityStudy, AddEscortShips; the reply is
/// offered whatever the room, unlike the Utwig's); in space before and
/// after the Kohr-Ah (SUPOX_INFO), and a greeting alone while they fight
/// them. Every path is checked against UQM's own script
/// (tests/golden/talks-supox.json).
/// </summary>
public sealed class SupoxScript : CommScript
{
    // races.h's SUPOX_SHIP and UTWIG_SHIP.
    private const int SupoxShip = 7, UtwigShip = 9;

    public SupoxScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private bool AtHome => (GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0;

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Peace);

        if (PlayerSaid(r, "bye_neutral"))
            NPCPhrase("GOODBYE_NEUTRAL");
        else if (PlayerSaid(r, "what_do_i_do_now"))
            NPCPhrase("FIX_IT");
        else if (PlayerSaid(r, "thanks_now_we_eat_you"))
        {
            NPCPhrase("HIDEOUS_MONSTERS");

            SetGameState("SUPOX_HOSTILE", 1);
            SetGameState("SUPOX_HOME_VISITS", 0);
            SetGameState("SUPOX_VISITS", 0);
        }
        else if (PlayerSaid(r, "bye_after_space"))
            NPCPhrase("GOODBYE_AFTER_SPACE");
        else if (PlayerSaid(r, "bye_before_space"))
            NPCPhrase("GOODBYE_BEFORE_SPACE");
        else if (PlayerSaid(r, "bye_allied_homeworld"))
            NPCPhrase("GOODBYE_ALLIED_HOMEWORLD");
        else if (PlayerSaid(r, "can_you_help"))
        {
            NPCPhrase("HOW_HELP");
            if (Sis.EscortRoom == 0)   // EscortFeasibilityStudy (SUPOX_SHIP)
                NPCPhrase("DONT_NEED");
            else
            {
                NPCPhrase("HAVE_4_SHIPS");

                T.AlienTalkSegue(Talk.WaitAll);
                T.AddEscortShips(SupoxShip, 4);
            }
        }
    }

    private void AlliedHome(int r)
    {
        int news = GetGameState("SUPOX_WAR_NEWS");
        int numVisits = GetGameState("UTWIG_SUPOX_MISSION");
        if (PlayerSaid(r, "how_went_war"))
        {
            NPCPhrase("ABOUT_BATTLE");

            news |= 1 << 0;
        }
        else if (PlayerSaid(r, "how_goes_war"))
        {
            if (numVisits == 1)
            {
                NPCPhrase("FLEET_ON_WAY");

                // Written back over by the news as read above, below.
                SetGameState("SUPOX_WAR_NEWS", 1);
            }
            else
            {
                switch (GetGameState("SUPOX_WAR_NEWS"))
                {
                    case 0:
                        NPCPhrase("BATTLE_HAPPENS_1");
                        news = 1;
                        break;
                    case 1:
                        NPCPhrase("BATTLE_HAPPENS_2");
                        news = 2;
                        break;
                }
            }

            DisablePhrase("how_goes_war");
        }
        else if (PlayerSaid(r, "learn_new_info"))
        {
            if (numVisits < 5)
                NPCPhrase("NO_NEW_INFO");
            else
            {
                NPCPhrase("SAMATRA");

                news |= 1 << 1;
            }

            DisablePhrase("learn_new_info");
        }
        else if (PlayerSaid(r, "what_now_homeworld"))
        {
            if (numVisits < 5)
                NPCPhrase("UP_TO_YOU");
            else
                NPCPhrase("HOPE_KILL_EACH_OTHER");

            DisablePhrase("what_now_homeworld");
        }
        else if (PlayerSaid(r, "how_is_ultron"))
        {
            NPCPhrase("ULTRON_IS_GREAT");

            DisablePhrase("how_is_ultron");
        }
        SetGameState("SUPOX_WAR_NEWS", news);

        if (numVisits >= 5)
        {
            if ((news & (1 << 0)) == 0)
                Response("how_went_war", AlliedHome);
        }
        else if (PhraseEnabled("how_goes_war")
                 && ((numVisits == 1 && news == 0) || (numVisits != 0 && news < 2)))
            Response("how_goes_war", AlliedHome);
        if (PhraseEnabled("learn_new_info"))
            Response("learn_new_info", AlliedHome);
        if (PhraseEnabled("what_now_homeworld"))
            Response("what_now_homeworld", AlliedHome);
        if (PhraseEnabled("how_is_ultron"))
            Response("how_is_ultron", AlliedHome);
        if (numVisits == 0)
            Response("can_you_help", ExitConversation);
        Response("bye_allied_homeworld", ExitConversation);
    }

    private void BeforeKohrAh(int r)
    {
        if (PlayerSaid(r, "whats_up_before_space"))
        {
            int numVisits = GetGameState("SUPOX_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_BEFORE_SPACE_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_BEFORE_SPACE_2");
                    --numVisits;
                    break;
            }
            SetGameState("SUPOX_INFO", numVisits);

            DisablePhrase("whats_up_before_space");
        }
        else if (PlayerSaid(r, "what_now_before_space"))
        {
            NPCPhrase("DO_THIS_BEFORE_SPACE");

            DisablePhrase("what_now_before_space");
        }

        if (PhraseEnabled("whats_up_before_space"))
            Response("whats_up_before_space", BeforeKohrAh);
        if (PhraseEnabled("what_now_before_space"))
            Response("what_now_before_space", BeforeKohrAh);
        Response("bye_before_space", ExitConversation);
    }

    private void AfterKohrAh(int r)
    {
        if (PlayerSaid(r, "whats_up_after_space"))
        {
            int numVisits = GetGameState("SUPOX_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_AFTER_SPACE_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_AFTER_SPACE_2");
                    --numVisits;
                    break;
            }
            SetGameState("SUPOX_INFO", numVisits);

            DisablePhrase("whats_up_after_space");
        }
        else if (PlayerSaid(r, "what_now_after_space"))
        {
            NPCPhrase("DO_THIS_AFTER_SPACE");

            DisablePhrase("what_now_after_space");
        }

        if (PhraseEnabled("whats_up_after_space"))
            Response("whats_up_after_space", AfterKohrAh);
        if (PhraseEnabled("what_now_after_space"))
            Response("what_now_after_space", AfterKohrAh);
        Response("bye_after_space", ExitConversation);
    }

    private void NeutralSupox(int r)
    {
        int lastStack = 0;
        var pStr = new string?[3];
        if (PlayerSaid(r, "i_am0"))
        {
            NPCPhrase("WE_ARE_SUPOX");

            SetGameState("SUPOX_STACK1", 1);
            DisablePhrase("i_am0");
        }
        else if (PlayerSaid(r, "my_ship0"))
        {
            NPCPhrase("OUR_SHIP");

            SetGameState("SUPOX_STACK1", 2);
            DisablePhrase("my_ship0");
        }
        else if (PlayerSaid(r, "from_alliance0"))
        {
            NPCPhrase("FROM_SUPOX");

            SetGameState("SUPOX_STACK1", 3);
            DisablePhrase("from_alliance0");
        }
        else if (PlayerSaid(r, "are_you_copying"))
        {
            NPCPhrase("YEAH_SORRY");

            SetGameState("SUPOX_STACK1", 4);
        }
        else if (PlayerSaid(r, "why_copy"))
        {
            NPCPhrase("SYMBIOTS");

            SetGameState("SUPOX_STACK1", 5);
        }
        else if (PlayerSaid(r, "tell_us_of_your_species"))
        {
            NPCPhrase("OUR_SPECIES");

            lastStack = 1;
            SetGameState("SUPOX_STACK2", 1);
        }
        else if (PlayerSaid(r, "plants_arent_intelligent"))
        {
            NPCPhrase("PROVES_WERE_SPECIAL");

            SetGameState("SUPOX_STACK2", 2);
        }
        else if (PlayerSaid(r, "anyone_around_here"))
        {
            NPCPhrase("UTWIG_NEARBY");

            lastStack = 2;
            SetGameState("SUPOX_WAR_NEWS", 1);
            T.StartSphereTracking(UtwigShip);
        }
        else if (PlayerSaid(r, "what_relation_to_utwig"))
        {
            NPCPhrase("UTWIG_ALLIES");

            lastStack = 2;
            SetGameState("SUPOX_WAR_NEWS", 1);
        }
        else if (PlayerSaid(r, "whats_wrong_with_utwig"))
        {
            NPCPhrase("BROKE_ULTRON");

            lastStack = 2;
            SetGameState("SUPOX_WAR_NEWS", 2);
        }
        else if (PlayerSaid(r, "whats_ultron"))
        {
            NPCPhrase("TAKE_ULTRON");

            // The broken Ultron handed over: the group's.
            SetGameState("SUPOX_WAR_NEWS", 0);
            SetGameState("ULTRON_CONDITION", 1);

            Response("what_do_i_do_now", ExitConversation);
            Response("thanks_now_we_eat_you", ExitConversation);

            return;
        }
        else if (PlayerSaid(r, "got_fixed_ultron"))
        {
            NPCPhrase("GOOD_GIVE_TO_UTWIG");

            SetGameState("SUPOX_ULTRON_HELP", 1);
        }
        else if (PlayerSaid(r, "look_i_repaired_lots"))
        {
            NPCPhrase("ALMOST_THERE");

            SetGameState("SUPOX_ULTRON_HELP", 1);
        }
        else if (PlayerSaid(r, "look_i_slightly_repaired"))
        {
            NPCPhrase("GREAT_DO_MORE");

            SetGameState("SUPOX_ULTRON_HELP", 1);
        }
        else if (PlayerSaid(r, "where_get_repairs"))
        {
            NPCPhrase("ANCIENT_RHYME");

            SetGameState("SUPOX_ULTRON_HELP", 1);
        }

        switch (GetGameState("SUPOX_STACK2"))
        {
            case 0:
                pStr[1] = "tell_us_of_your_species";
                break;
            case 1:
                pStr[1] = "plants_arent_intelligent";
                break;
        }
        // Each of the first stack's replies takes the second stack's away (pStr[1] = 0) until it's through.
        switch (GetGameState("SUPOX_STACK1"))
        {
            case 0:
                T.ConstructResponse("i_am0", Sis.CaptainName, "i_am1");
                pStr[0] = "i_am0";
                pStr[1] = null;
                break;
            case 1:
                T.ConstructResponse("my_ship0", Sis.ShipName, "my_ship1");
                pStr[0] = "my_ship0";
                pStr[1] = null;
                break;
            case 2:
                T.ConstructResponse("from_alliance0", T.GetAllianceName("name_1"), "from_alliance1");
                pStr[0] = "from_alliance0";
                pStr[1] = null;
                break;
            case 3:
                pStr[0] = "are_you_copying";
                pStr[1] = null;
                break;
            case 4:
                pStr[0] = "why_copy";
                pStr[1] = null;
                break;
        }
        int ultron = GetGameState("ULTRON_CONDITION");
        if (ultron == 0)
        {
            switch (GetGameState("SUPOX_WAR_NEWS"))
            {
                case 0:
                    if (GetGameState("UTWIG_VISITS") != 0
                        || GetGameState("UTWIG_HOME_VISITS") != 0
                        || GetGameState("BOMB_VISITS") != 0)
                        pStr[2] = "what_relation_to_utwig";
                    else
                        pStr[2] = "anyone_around_here";
                    break;
                case 1:
                    pStr[2] = "whats_wrong_with_utwig";
                    break;
                case 2:
                    pStr[2] = "whats_ultron";
                    break;
            }
        }
        // The first three of the first stack are spliced with a name (shared_phrase_buf).
        bool spliced = GetGameState("SUPOX_STACK1") <= 2;
        if (pStr[lastStack] is { } last)
            Offer(lastStack, last);
        for (int i = 0; i < 3; ++i)
        {
            if (i != lastStack && pStr[i] is { } other)
                Offer(i, other);
        }
        if (GetGameState("SUPOX_ULTRON_HELP") == 0)
        {
            switch (ultron)
            {
                case 1:
                    Response("where_get_repairs", NeutralSupox);
                    break;
                case 2:
                    Response("look_i_slightly_repaired", NeutralSupox);
                    break;
                case 3:
                    Response("look_i_repaired_lots", NeutralSupox);
                    break;
                case 4:
                    Response("got_fixed_ultron", NeutralSupox);
                    break;
            }
        }
        Response("bye_neutral", ExitConversation);

        void Offer(int stack, string id)
        {
            if (stack != 0 || !spliced)
                Response(id, NeutralSupox);
            else
                T.DoResponsePhrase(id, NeutralSupox, Talk.SharedPhraseBuffer);
        }
    }

    // The greetings by visit: each switch's last case keeps its count there.
    private void Greet(string field, params string[] hellos)
    {
        int numVisits = GetGameState(field);
        if (numVisits < hellos.Length)
            NPCPhrase(hellos[numVisits]);
        if (numVisits == hellos.Length - 1)
            --numVisits;
        ++numVisits;
        SetGameState(field, numVisits);
    }

    protected internal override void Intro()
    {
        if (T.Activity == TalkActivity.WonLastBattle)
        {
            NPCPhrase("OUT_TAKES");

            T.SetSegue(Segue.Peace);
            return;
        }

        if (GetGameState("SUPOX_HOSTILE") != 0)
        {
            // Wherever they're met; no replies: the greeting is the whole
            // talk, and it ends in peace.
            Greet("SUPOX_VISITS", "HOSTILE_SPACE_HELLO_1", "HOSTILE_SPACE_HELLO_2");

            T.SetSegue(Segue.Peace);
        }
        else if (T.CheckAlliance(SupoxShip) == Story.Allied.Good)
        {
            if (AtHome)
            {
                Greet("SUPOX_HOME_VISITS", "ALLIED_HOMEWORLD_HELLO_1", "ALLIED_HOMEWORLD_HELLO_2",
                    "ALLIED_HOMEWORLD_HELLO_3", "ALLIED_HOMEWORLD_HELLO_4");

                AlliedHome(0);
            }
            else
            {
                int mission = GetGameState("UTWIG_SUPOX_MISSION");
                if (mission == 1)
                {
                    Greet("SUPOX_VISITS", "HELLO_BEFORE_KOHRAH_SPACE_1", "HELLO_BEFORE_KOHRAH_SPACE_2");

                    BeforeKohrAh(0);
                }
                else if (mission < 5)
                {
                    // No replies: the greeting is the whole talk.
                    Greet("SUPOX_VISITS", "HELLO_DURING_KOHRAH_SPACE_1", "HELLO_DURING_KOHRAH_SPACE_2");
                }
                else
                {
                    Greet("SUPOX_VISITS", "HELLO_AFTER_KOHRAH_SPACE_1", "HELLO_AFTER_KOHRAH_SPACE_2");

                    AfterKohrAh(0);
                }
            }
        }
        else
        {
            if (AtHome)
                Greet("SUPOX_HOME_VISITS", "NEUTRAL_HOMEWORLD_HELLO_1", "NEUTRAL_HOMEWORLD_HELLO_2");
            else
                Greet("SUPOX_VISITS", "NEUTRAL_SPACE_HELLO_1", "NEUTRAL_SPACE_HELLO_2");

            NeutralSupox(0);
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if (GetGameState("SUPOX_HOSTILE") == 0 || T.Activity == TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
