// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/yehat/yehatc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Yehat (yehatc.c), whole: met in space (YEHAT_VISITS) and at their
/// homeworld (GLOBAL_FLAGS_AND_DATA bit 7, YEHAT_HOME_VISITS), hostile
/// unless the endgame's battle is won. In space: their news
/// (YEHAT_REBEL_INFO), help and the alliance asked (NO_YEHAT_HELP_SPACE,
/// NO_YEHAT_ALLY_SPACE, the captain's, ship's and alliance's names spliced
/// in; "obligation" lets the player go in peace), the Pkunk asked about
/// once the players have met them at home and in space, the farewell
/// whose peace is a draw of the talk's own generator; and once Tanaka has
/// been met (SHOFIXTI_VISITS), the Shofixti alive: the revolt's offer, which
/// needs a Shofixti ship in the escort fleet (HaveEscortShip: the host's,
/// whoever answers) to be sent over (YEHAT_CIVIL_WAR and the
/// YEHAT_REBEL_EVENT, set aside until stage 3c), else "not here", or
/// refused. At home: their news, help, information and the alliance
/// refused. Once the civil war has begun, the royalists' talks (their
/// greetings at home and in space, the rebellion, the Pkunk, the
/// apology). Every path is checked against UQM's own script
/// (tests/golden/talks-yehat.json).
/// </summary>
public sealed class YehatScript : CommScript
{
    // races.h's SHOFIXTI_SHIP.
    private const int ShofixtiShip = 5;

    public YehatScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Hostile);

        if (PlayerSaid(r, "bye_homeworld"))
            NPCPhrase("GOODBYE_AND_DIE_HOMEWORLD");
        else if (PlayerSaid(r, "bye_royalist"))
            NPCPhrase("GOODBYE_AND_DIE_ROYALIST");
        else if (PlayerSaid(r, "i_demand_you_ally_homeworld0"))
        {
            NPCPhrase("ENEMY_MUST_DIE");

            SetGameState("NO_YEHAT_ALLY_HOME", 1);
        }
        else if (PlayerSaid(r, "bye_space"))
        {
            if (((byte)T.Random() & 1) != 0)
                NPCPhrase("GOODBYE_AND_DIE_SPACE");
            else
            {
                NPCPhrase("GO_IN_PEACE");

                T.SetSegue(Segue.Peace);
            }
        }
        else if (PlayerSaid(r, "not_here") || PlayerSaid(r, "not_send"))
        {
            switch (GetGameState("YEHAT_REBEL_VISITS"))
            {
                case 0:
                    NPCPhrase("JUST_A_TRICK_1");
                    break;
                case 1:
                    NPCPhrase("JUST_A_TRICK_2");
                    break;
            }
            SetGameState("YEHAT_REBEL_VISITS", 1);
        }
        else if (PlayerSaid(r, "ok_send"))
        {
            NPCPhrase("WE_REVOLT");

            T.SetSegue(Segue.Peace);
            SetGameState("YEHAT_CIVIL_WAR", 1);
            SetGameState("YEHAT_VISITS", 0);
            SetGameState("YEHAT_HOME_VISITS", 0);
            SetGameState("YEHAT_REBEL_VISITS", 0);
            SetGameState("YEHAT_REBEL_INFO", 0);
            SetGameState("YEHAT_REBEL_TOLD_PKUNK", 0);
            SetGameState("NO_YEHAT_INFO", 0);

            // The revolt: set aside until stage 3c runs the calendar.
            T.AddEvent(relative: true, 0, 0, 0, "YEHAT_REBEL_EVENT");
        }
    }

    // Whether the players have met the Pkunk in space and at home, and not yet asked the Yehat about them.
    private bool MayAskAboutPkunk =>
        GetGameState("YEHAT_ROYALIST_TOLD_PKUNK") == 0 && GetGameState("PKUNK_VISITS") != 0
        && GetGameState("PKUNK_HOME_VISITS") != 0;

    private void Royalists(int r)
    {
        if (PlayerSaid(r, "how_is_rebellion"))
        {
            int numVisits = GetGameState("YEHAT_ROYALIST_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("ROYALIST_REBELLION_1");
                    break;
                case 1:
                    NPCPhrase("ROYALIST_REBELLION_2");
                    --numVisits;
                    break;
            }
            SetGameState("YEHAT_ROYALIST_INFO", numVisits);

            DisablePhrase("how_is_rebellion");
        }
        else if (PlayerSaid(r, "what_about_pkunk_royalist"))
        {
            if (GetGameState("YEHAT_ABSORBED_PKUNK") != 0)
                NPCPhrase("PKUNK_ABSORBED_ROYALIST");
            else
                NPCPhrase("HATE_PKUNK_ROYALIST");

            SetGameState("YEHAT_ROYALIST_TOLD_PKUNK", 1);
        }
        else if (PlayerSaid(r, "sorry_about_revolution"))
        {
            NPCPhrase("ALL_YOUR_FAULT");

            SetGameState("NO_YEHAT_INFO", 1);
        }

        if (PhraseEnabled("how_is_rebellion"))
            Response("how_is_rebellion", Royalists);
        if (MayAskAboutPkunk)
            Response("what_about_pkunk_royalist", Royalists);
        if (GetGameState("NO_YEHAT_INFO") == 0)
            Response("sorry_about_revolution", Royalists);
        Response("bye_royalist", ExitConversation);
    }

    private void StartRevolt(int r)
    {
        if (PlayerSaid(r, "shofixti_alive_1"))
        {
            NPCPhrase("SEND_HIM_OVER_1");

            SetGameState("YEHAT_REBEL_TOLD_PKUNK", 1);
        }
        else if (PlayerSaid(r, "shofixti_alive_2"))
            NPCPhrase("SEND_HIM_OVER_2");

        // The host's fleet, whoever answers (the stage 3b-2 plan's question 4).
        if (T.HaveEscortShip(ShofixtiShip))
            Response("ok_send", ExitConversation);
        else
            Response("not_here", ExitConversation);
        Response("not_send", ExitConversation);
    }

    private void YehatHome(int r)
    {
        if (PlayerSaid(r, "whats_up_homeworld"))
        {
            int numVisits = GetGameState("YEHAT_ROYALIST_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_HOMEWORLD_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_HOMEWORLD_2");
                    --numVisits;
                    break;
            }
            SetGameState("YEHAT_ROYALIST_INFO", numVisits);

            DisablePhrase("whats_up_homeworld");
        }
        else if (PlayerSaid(r, "at_least_help_us_homeworld"))
        {
            NPCPhrase("NO_HELP_ENEMY");

            SetGameState("NO_YEHAT_HELP_HOME", 1);
        }
        else if (PlayerSaid(r, "give_info"))
        {
            NPCPhrase("NO_INFO_FOR_ENEMY");

            SetGameState("NO_YEHAT_INFO", 1);
        }
        else if (PlayerSaid(r, "what_about_pkunk_royalist"))
        {
            if (GetGameState("YEHAT_ABSORBED_PKUNK") != 0)
                NPCPhrase("PKUNK_ABSORBED_ROYALIST");
            else
                NPCPhrase("HATE_PKUNK_ROYALIST");

            SetGameState("YEHAT_ROYALIST_TOLD_PKUNK", 1);
        }

        if (PhraseEnabled("whats_up_homeworld"))
            Response("whats_up_homeworld", YehatHome);
        if (MayAskAboutPkunk)
            Response("what_about_pkunk_royalist", YehatHome);
        if (GetGameState("NO_YEHAT_HELP_HOME") == 0)
            Response("at_least_help_us_homeworld", YehatHome);
        if (GetGameState("NO_YEHAT_INFO") == 0)
            Response("give_info", YehatHome);
        if (GetGameState("NO_YEHAT_ALLY_HOME") == 0)
        {
            T.ConstructResponse("i_demand_you_ally_homeworld0", Sis.CaptainName, "i_demand_you_ally_homeworld1",
                T.GetAllianceName("name_1"), "i_demand_you_ally_homeworld2", Sis.ShipName,
                "i_demand_you_ally_homeworld3");
            T.DoResponsePhrase("i_demand_you_ally_homeworld0", ExitConversation, Talk.SharedPhraseBuffer);
        }
        Response("bye_homeworld", ExitConversation);
    }

    private void YehatSpace(int r)
    {
        int lastStack = 0;
        var pStr = new string?[3];
        if (PlayerSaid(r, "whats_up_space_1") || PlayerSaid(r, "whats_up_space_2")
            || PlayerSaid(r, "whats_up_space_3") || PlayerSaid(r, "whats_up_space_4"))
        {
            int numVisits = GetGameState("YEHAT_REBEL_INFO");
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
                    --numVisits;
                    break;
            }
            SetGameState("YEHAT_REBEL_INFO", numVisits);

            DisablePhrase("whats_up_space_1");
        }
        else if (PlayerSaid(r, "i_demand_you_ally_space0"))
        {
            NPCPhrase("WE_CANNOT_1");

            lastStack = 2;
            SetGameState("NO_YEHAT_ALLY_SPACE", 1);
        }
        else if (PlayerSaid(r, "obligation"))
        {
            NPCPhrase("WE_CANNOT_2");

            T.SetSegue(Segue.Peace);
            SetGameState("NO_YEHAT_ALLY_SPACE", 2);

            return;
        }
        else if (PlayerSaid(r, "at_least_help_us_space"))
        {
            NPCPhrase("SORRY_CANNOT");

            lastStack = 1;
            SetGameState("NO_YEHAT_HELP_SPACE", 1);
        }
        else if (PlayerSaid(r, "dishonor"))
        {
            NPCPhrase("HERES_A_HINT");

            SetGameState("NO_YEHAT_HELP_SPACE", 2);
        }
        else if (PlayerSaid(r, "what_about_pkunk_royalist"))
        {
            if (GetGameState("YEHAT_ABSORBED_PKUNK") != 0)
                NPCPhrase("PKUNK_ABSORBED_ROYALIST");
            else
                NPCPhrase("HATE_PKUNK_ROYALIST");

            SetGameState("YEHAT_ROYALIST_TOLD_PKUNK", 1);
        }

        if (PhraseEnabled("whats_up_space_1"))
        {
            switch (GetGameState("YEHAT_REBEL_INFO"))
            {
                case 0:
                    pStr[0] = "whats_up_space_1";
                    break;
                case 1:
                    pStr[0] = "whats_up_space_2";
                    break;
                case 2:
                    pStr[0] = "whats_up_space_3";
                    break;
                case 3:
                    pStr[0] = "whats_up_space_4";
                    break;
            }
        }
        switch (GetGameState("NO_YEHAT_HELP_SPACE"))
        {
            case 0:
                pStr[1] = "at_least_help_us_space";
                break;
            case 1:
                pStr[1] = "dishonor";
                break;
        }
        switch (GetGameState("NO_YEHAT_ALLY_SPACE"))
        {
            case 0:
                T.ConstructResponse("i_demand_you_ally_space0", Sis.CaptainName, "i_demand_you_ally_space1",
                    Sis.ShipName, "i_demand_you_ally_space2", T.GetAllianceName("name_1"),
                    "i_demand_you_ally_space3");
                pStr[2] = "i_demand_you_ally_space0";
                break;
            case 1:
                pStr[2] = "obligation";
                break;
        }

        if (pStr[lastStack] is { } last)
            Offer(last);
        for (int i = 0; i < 3; ++i)
        {
            if (i != lastStack && pStr[i] is { } s)
                Offer(s);
        }
        if (MayAskAboutPkunk)
            Response("what_about_pkunk_royalist", YehatSpace);
        if (GetGameState("SHOFIXTI_VISITS") != 0)
        {
            switch (GetGameState("YEHAT_REBEL_TOLD_PKUNK"))
            {
                case 0:
                    Response("shofixti_alive_1", StartRevolt);
                    break;
                case 1:
                    Response("shofixti_alive_2", StartRevolt);
                    break;
            }
        }
        Response("bye_space", ExitConversation);
    }

    // YehatSpace's stacks offered: the alliance's demand with its names (shared_phrase_buf), the others plain.
    private void Offer(string phrase)
    {
        if (phrase != "i_demand_you_ally_space0")
            Response(phrase, YehatSpace);
        else
            T.DoResponsePhrase(phrase, YehatSpace, Talk.SharedPhraseBuffer);
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

        if (GetGameState("YEHAT_CIVIL_WAR") != 0)
        {
            if ((GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0)
            {
                numVisits = GetGameState("YEHAT_HOME_VISITS");
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("ROYALIST_HOMEWORLD_HELLO_1");
                        break;
                    case 1:
                        NPCPhrase("ROYALIST_HOMEWORLD_HELLO_2");
                        --numVisits;
                        break;
                }
                SetGameState("YEHAT_HOME_VISITS", numVisits);
            }
            else
            {
                numVisits = GetGameState("YEHAT_VISITS");
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("ROYALIST_SPACE_HELLO_1");
                        break;
                    case 1:
                        NPCPhrase("ROYALIST_SPACE_HELLO_2");
                        --numVisits;
                        break;
                }
                SetGameState("YEHAT_VISITS", numVisits);
            }

            Royalists(0);
        }
        else if ((GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0)
        {
            numVisits = GetGameState("YEHAT_HOME_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("HOMEWORLD_HELLO_1");
                    break;
                case 1:
                    NPCPhrase("HOMEWORLD_HELLO_2");
                    --numVisits;
                    break;
            }
            SetGameState("YEHAT_HOME_VISITS", numVisits);

            YehatHome(0);
        }
        else
        {
            numVisits = GetGameState("YEHAT_VISITS");
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
            SetGameState("YEHAT_VISITS", numVisits);

            YehatSpace(0);
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        T.SetSegue(T.Activity != TalkActivity.WonLastBattle ? Segue.Hostile : Segue.Peace);
    }
}
