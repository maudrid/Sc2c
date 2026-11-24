// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/orz/orzc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Orz (orzc.c), whole: in space, at their homeworld
/// (GLOBAL_FLAGS_AND_DATA bit 7) and at the Taalo protector's moon (bit 6,
/// genorz.c's 14 guards). The first meeting's hello and the computer's
/// report after it (MET_ORZ_BEFORE: the talking animation off for it); the
/// neutral talk (ORZ_ANDRO_STATE, ORZ_STACK0 and 1, REFUSED_ORZ_ALLIANCE)
/// and the alliance made (SetRaceAllied); the allies' questions
/// (ORZ_GENERAL_INFO, ORZ_PERSONAL_INFO) and the break over the Androsynth:
/// asked about them a third time, or pressed while neutral, they turn on
/// the players for good (ORZ_MANNER 2: their colours faded to the angry
/// map), the alliance ended and every Orz ship taken out of the escort
/// fleet (RemoveEscortShips: the host's, whoever answers, the stage 3b-2
/// plan's question 4); their anger after a fight (post_orz_enc: ORZ_MANNER
/// 1), the insults and the apology that makes the alliance again; at the
/// Taalo moon their questions, and leave to land for allies
/// (TAALO_UNPROTECTED). Every path is checked against UQM's own script
/// (tests/golden/talks-orz.json).
/// </summary>
public sealed class OrzScript : CommScript
{
    // races.h's ORZ_SHIP.
    private const int OrzShip = 3;

    public OrzScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Peace);

        if (PlayerSaid(r, "bye_ally"))
            NPCPhrase("GOODBYE_ALLY");
        else if (PlayerSaid(r, "bye_neutral"))
            NPCPhrase("GOODBYE_NEUTRAL");
        else if (PlayerSaid(r, "bye_angry"))
            NPCPhrase("GOODBYE_ANGRY");
        else if (PlayerSaid(r, "bye_taalo"))
        {
            if (GetGameState("ORZ_MANNER") == 1)
                NPCPhrase("ANGRY_TAALO_GOODBYE");
            else
                NPCPhrase("FRIENDLY_TAALO_GOODBYE");
        }
        else if (PlayerSaid(r, "hostile_2"))
        {
            NPCPhrase("HOSTILITY_IS_BAD_2");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "may_we_land"))
        {
            NPCPhrase("SURE_LAND");

            SetGameState("TAALO_UNPROTECTED", 1);
        }
        else if (PlayerSaid(r, "yes_alliance") || PlayerSaid(r, "were_sorry"))
        {
            if (PlayerSaid(r, "yes_alliance"))
                NPCPhrase("GREAT");
            else
                NPCPhrase("APOLOGY_ACCEPTED");

            SetGameState("ORZ_ANDRO_STATE", 0);
            SetGameState("ORZ_GENERAL_INFO", 0);
            SetGameState("ORZ_PERSONAL_INFO", 0);
            SetGameState("ORZ_MANNER", 3);
            T.SetRaceAllied(OrzShip, true);
        }
        else if (PlayerSaid(r, "demand_to_land"))
        {
            NPCPhrase("NO_DEMAND");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "about_andro_3") || PlayerSaid(r, "must_know_about_androsyn"))
        {
            if (PlayerSaid(r, "about_andro_3"))
                NPCPhrase("BLEW_IT");
            else
                NPCPhrase("KNOW_TOO_MUCH");

            SetGameState("ORZ_VISITS", 0);
            SetGameState("ORZ_MANNER", 2);
            T.SetSegue(Segue.Hostile);
            if (PlayerSaid(r, "about_andro_3"))
            {
                // The alliance's break: every Orz ship leaves the escort
                // fleet (the host's, whoever answers).
                T.SetRaceAllied(OrzShip, false);
                T.RemoveEscortShips(OrzShip);
            }

            T.XFormColorMap(1, Talk.OneSecond / 2);
        }
        else // insults
        {
            int numVisits = GetGameState("ORZ_PERSONAL_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("INSULTED_1");
                    break;
                case 1:
                    NPCPhrase("INSULTED_2");
                    break;
                case 2:
                    NPCPhrase("INSULTED_3");
                    T.SetSegue(Segue.Hostile);
                    break;
                case 7:
                    --numVisits;
                    goto default;
                default:
                    NPCPhrase("INSULTED_4");
                    T.SetSegue(Segue.Hostile);
                    break;
            }
            SetGameState("ORZ_PERSONAL_INFO", numVisits);
        }
    }

    private void TaaloWorld(int r)
    {
        // We can only get here when ORZ_MANNER != HOSTILE (2).
        int manner = GetGameState("ORZ_MANNER");
        if (PlayerSaid(r, "demand_to_land"))
        {
            NPCPhrase("ASK_NICELY");

            DisablePhrase("demand_to_land");
        }
        else if (PlayerSaid(r, "why_you_here"))
        {
            if (manner != 1)
                NPCPhrase("FRIENDLY_EXPLANATION");
            else
                NPCPhrase("ANGRY_EXPLANATION");

            DisablePhrase("why_you_here");
        }
        else if (PlayerSaid(r, "what_is_this_place"))
        {
            if (manner != 1)
                NPCPhrase("FRIENDLY_PLACE");
            else
                NPCPhrase("ANGRY_PLACE");

            DisablePhrase("what_is_this_place");
        }
        else if (PlayerSaid(r, "may_we_land"))
        {
            NPCPhrase("ALLIES_CAN_VISIT");

            DisablePhrase("may_we_land");
        }
        else if (PlayerSaid(r, "make_alliance"))
        {
            NPCPhrase("CANT_ALLY_HERE");

            DisablePhrase("make_alliance");
        }
        else if (PlayerSaid(r, "why_busy"))
        {
            NPCPhrase("BUSY_BECAUSE");

            DisablePhrase("why_busy");
        }

        if (PhraseEnabled("may_we_land"))
        {
            if (manner == 3 && T.CheckAlliance(OrzShip) == Story.Allied.Good)
                Response("may_we_land", ExitConversation);
            else
                Response("may_we_land", TaaloWorld);
        }
        else if (PhraseEnabled("make_alliance"))
            Response("make_alliance", TaaloWorld);
        else if (PhraseEnabled("why_busy"))
            Response("why_busy", TaaloWorld);
        if (PhraseEnabled("demand_to_land"))
        {
            if (manner == 1)
                Response("demand_to_land", ExitConversation);
            else
                Response("demand_to_land", TaaloWorld);
        }
        if (PhraseEnabled("why_you_here"))
            Response("why_you_here", TaaloWorld);
        if (PhraseEnabled("what_is_this_place"))
            Response("what_is_this_place", TaaloWorld);
        Response("bye_taalo", ExitConversation);
    }

    private void OrzAllied(int r)
    {
        int numVisits;

        if (PlayerSaid(r, "whats_up_ally"))
        {
            numVisits = GetGameState("ORZ_GENERAL_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_ALLY_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_ALLY_2");
                    break;
                case 2:
                    NPCPhrase("GENERAL_INFO_ALLY_3");
                    break;
                case 3:
                    NPCPhrase("GENERAL_INFO_ALLY_4");
                    --numVisits;
                    break;
            }
            SetGameState("ORZ_GENERAL_INFO", numVisits);

            DisablePhrase("whats_up_ally");
        }
        else if (PlayerSaid(r, "more_about_you"))
        {
            numVisits = GetGameState("ORZ_PERSONAL_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("ABOUT_US_1");
                    break;
                case 1:
                    NPCPhrase("ABOUT_US_2");
                    break;
                case 2:
                    NPCPhrase("ABOUT_US_3");
                    break;
                case 3:
                    NPCPhrase("ABOUT_US_4");
                    --numVisits;
                    break;
            }
            SetGameState("ORZ_PERSONAL_INFO", numVisits);

            DisablePhrase("more_about_you");
        }
        else if (PlayerSaid(r, "about_andro_1"))
        {
            NPCPhrase("FORGET_ANDRO_1");

            SetGameState("ORZ_ANDRO_STATE", 1);
        }
        else if (PlayerSaid(r, "about_andro_2"))
        {
            NPCPhrase("FORGET_ANDRO_2");

            SetGameState("ORZ_ANDRO_STATE", 2);
        }

        if (GetGameState("ORZ_ANDRO_STATE") == 0)
            Response("about_andro_1", OrzAllied);
        else if (GetGameState("ORZ_ANDRO_STATE") == 1)
            Response("about_andro_2", OrzAllied);
        else
            Response("about_andro_3", ExitConversation);
        if (PhraseEnabled("whats_up_ally"))
            Response("whats_up_ally", OrzAllied);
        if (PhraseEnabled("more_about_you"))
            Response("more_about_you", OrzAllied);
        Response("bye_ally", ExitConversation);
    }

    private void WhereAndrosyn(int r)
    {
        NPCPhrase("DISEMBLE_ABOUT_ANDROSYN");
        DisablePhrase("where_androsyn");

        Response("must_know_about_androsyn", ExitConversation);
        Response("dont_really_care", OrzNeutral);
    }

    private void OfferAlliance(int r)
    {
        if (PlayerSaid(r, "seem_like_nice_guys"))
            NPCPhrase("ARE_NICE_WANT_ALLY");
        else if (PlayerSaid(r, "talk_about_alliance"))
            NPCPhrase("OK_TALK_ALLIANCE");
        else if (PlayerSaid(r, "why_so_trusting"))
        {
            NPCPhrase("TRUSTING_BECAUSE");

            SetGameState("ORZ_STACK1", 1);
        }

        Response("no_alliance", OrzNeutral);
        Response("decide_later", OrzNeutral);
        if (GetGameState("ORZ_STACK1") == 0)
            Response("why_so_trusting", OfferAlliance);
        Response("yes_alliance", ExitConversation);
    }

    private void OrzNeutral(int r)
    {
        int lastStack = 0;
        var pStr = new string?[3];
        if (PlayerSaid(r, "hostile_1"))
        {
            NPCPhrase("HOSTILITY_IS_BAD_1");

            DisablePhrase("hostile_1");
            lastStack = 2;
        }
        else if (PlayerSaid(r, "we_are_vindicator0"))
        {
            NPCPhrase("NICE_TO_MEET_YOU");

            SetGameState("ORZ_STACK0", 1);
            lastStack = 1;
        }
        else if (PlayerSaid(r, "who_you"))
        {
            NPCPhrase("WE_ARE_ORZ");

            SetGameState("ORZ_ANDRO_STATE", 1);
        }
        else if (PlayerSaid(r, "why_here"))
        {
            NPCPhrase("HERE_BECAUSE");

            SetGameState("ORZ_ANDRO_STATE", 2);
        }
        else if (PlayerSaid(r, "no_alliance"))
        {
            NPCPhrase("MAYBE_LATER");

            DisablePhrase("talk_about_alliance");
            SetGameState("REFUSED_ORZ_ALLIANCE", 1);
        }
        else if (PlayerSaid(r, "decide_later"))
        {
            NPCPhrase("OK_LATER");

            DisablePhrase("talk_about_alliance");
            SetGameState("REFUSED_ORZ_ALLIANCE", 1);
        }
        else if (PlayerSaid(r, "dont_really_care"))
            NPCPhrase("YOU_ARE_OUR_FRIENDS");
        else if (PlayerSaid(r, "where_androsyn"))
        {
            WhereAndrosyn(r);
            return;
        }
        else if (PlayerSaid(r, "talk_about_alliance") || PlayerSaid(r, "seem_like_nice_guys"))
        {
            OfferAlliance(r);
            return;
        }
        else if (PlayerSaid(r, "hostile_2"))
        {
            ExitConversation(r);
            return;
        }

        if (GetGameState("ORZ_ANDRO_STATE") == 0)
            pStr[0] = "who_you";
        else if (GetGameState("ORZ_ANDRO_STATE") == 1)
            pStr[0] = "why_here";
        else if (PhraseEnabled("where_androsyn") && GetGameState("ORZ_ANDRO_STATE") == 2)
            pStr[0] = "where_androsyn";
        if (GetGameState("REFUSED_ORZ_ALLIANCE") != 0)
        {
            if (PhraseEnabled("talk_about_alliance"))
                pStr[1] = "talk_about_alliance";
        }
        else if (GetGameState("ORZ_STACK0") == 0)
        {
            T.ConstructResponse("we_are_vindicator0", Sis.CaptainName, "we_are_vindicator1",
                Sis.ShipName, "we_are_vindicator2");
            pStr[1] = "we_are_vindicator0";
        }
        else
            pStr[1] = "seem_like_nice_guys";
        if (PhraseEnabled("hostile_1"))
            pStr[2] = "hostile_1";
        else
            pStr[2] = "hostile_2";

        if (pStr[lastStack] is { } last)
            Respond(last);
        for (int i = 0; i < 3; ++i)
        {
            if (i != lastStack && pStr[i] is { } p)
                Respond(p);
        }
        Response("bye_neutral", ExitConversation);
    }

    // OrzNeutral's reply from its stacks: we_are_vindicator0 with the
    // names spliced in (DoResponsePhrase with shared_phrase_buf).
    private void Respond(string phrase)
    {
        if (phrase != "we_are_vindicator0")
            Response(phrase, OrzNeutral);
        else
            T.DoResponsePhrase(phrase, OrzNeutral, Talk.SharedPhraseBuffer);
    }

    private void OrzAngry(int r)
    {
        if (PlayerSaid(r, "whats_up_angry"))
        {
            int numVisits = GetGameState("ORZ_GENERAL_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_ANGRY_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_ANGRY_2");
                    --numVisits;
                    break;
            }
            SetGameState("ORZ_GENERAL_INFO", numVisits);

            DisablePhrase("whats_up_angry");
        }

        if (PhraseEnabled("whats_up_angry"))
            Response("whats_up_angry", OrzAngry);
        Response("were_sorry", ExitConversation);
        switch (GetGameState("ORZ_PERSONAL_INFO"))
        {
            case 0:
                Response("insult_1", ExitConversation);
                break;
            case 1:
                Response("insult_2", ExitConversation);
                break;
            case 2:
                Response("insult_3", ExitConversation);
                break;
            case 3:
                Response("insult_4", ExitConversation);
                break;
            case 4:
                Response("insult_5", ExitConversation);
                break;
            case 5:
                Response("insult_6", ExitConversation);
                break;
            case 6:
                Response("insult_7", ExitConversation);
                break;
            case 7:
                Response("insult_8", ExitConversation);
                break;
        }
        Response("bye_angry", ExitConversation);
    }

    // The greetings by visit, at home (ORZ_HOME_VISITS) or in space
    // (ORZ_VISITS): each switch's last case keeps its count there.
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

        if (GetGameState("MET_ORZ_BEFORE") == 0)
            NPCPhrase("INIT_HELLO");

        int manner = GetGameState("ORZ_MANNER");
        bool atHome = (GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0;
        if (manner == 2)
        {
            T.SetColorMap(1);

            Greet("ORZ_VISITS", "HOSTILE_HELLO_1", "HOSTILE_HELLO_2");

            T.SetSegue(Segue.Hostile);
        }
        else if ((GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 6)) != 0)
        {
            if (manner != 1)
                Greet("TAALO_VISITS", "FRIENDLY_ALLIED_TAALO_HELLO_1", "FRIENDLY_ALLIED_TAALO_HELLO_2");
            else
                Greet("TAALO_VISITS", "ANGRY_TAALO_HELLO_1", "ANGRY_TAALO_HELLO_2");

            TaaloWorld(0);
        }
        else if (manner == 3 && T.CheckAlliance(OrzShip) == Story.Allied.Good)
        {
            if (atHome)
                Greet("ORZ_HOME_VISITS", "ALLIED_HOMEWORLD_HELLO_1", "ALLIED_HOMEWORLD_HELLO_2",
                    "ALLIED_HOMEWORLD_HELLO_3", "ALLIED_HOMEWORLD_HELLO_4");
            else
                Greet("ORZ_VISITS", "ALLIED_SPACE_HELLO_1", "ALLIED_SPACE_HELLO_2", "ALLIED_SPACE_HELLO_3",
                    "ALLIED_SPACE_HELLO_4");

            OrzAllied(0);
        }
        else if (manner != 1)
        {
            if (atHome)
                Greet("ORZ_HOME_VISITS", "NEUTRAL_HOMEWORLD_HELLO_1", "NEUTRAL_HOMEWORLD_HELLO_2",
                    "NEUTRAL_HOMEWORLD_HELLO_3", "NEUTRAL_HOMEWORLD_HELLO_4");
            else
                Greet("ORZ_VISITS", "NEUTRAL_SPACE_HELLO_1", "NEUTRAL_SPACE_HELLO_2", "NEUTRAL_SPACE_HELLO_3",
                    "NEUTRAL_SPACE_HELLO_4");

            OrzNeutral(0);
        }
        else
        {
            if (atHome)
                Greet("ORZ_HOME_VISITS", "ANGRY_HOMEWORLD_HELLO_1", "ANGRY_HOMEWORLD_HELLO_2");
            else
                Greet("ORZ_VISITS", "ANGRY_SPACE_HELLO_1", "ANGRY_SPACE_HELLO_2");

            OrzAngry(0);
        }

        if (GetGameState("MET_ORZ_BEFORE") == 0)
        {
            SetGameState("MET_ORZ_BEFORE", 1);

            // Disable talking anim and run the computer report.
            T.EnableTalkingAnim(false);
            T.AlienTalkSegue(1);
            // Run whatever is left with talking anim.
            T.EnableTalkingAnim(true);
        }
    }

    // post_orz_enc: a hostile ending (a fight, or a hostile reply) angers
    // them (ORZ_MANNER 1, their greetings counted afresh) unless they're
    // already their enemies for good.
    protected internal override void PostEncounter()
    {
        int manner;
        if (T.Segue == Segue.Hostile && (manner = GetGameState("ORZ_MANNER")) != 2)
        {
            SetGameState("ORZ_MANNER", 1);
            if (manner != 1)
            {
                SetGameState("ORZ_VISITS", 0);
                SetGameState("ORZ_HOME_VISITS", 0);
                SetGameState("TAALO_VISITS", 0);
            }
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if (GetGameState("ORZ_MANNER") == 3 || T.Activity == TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
