// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/pkunk/pkunkc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Pkunk (pkunkc.c), whole: in space (neutral, friendly, or migrating
/// while their mission has them on the move: PKUNK_MISSION odd, stage
/// 3c's) and at their homeworld (GLOBAL_FLAGS_AND_DATA bit 7). At home the
/// Clear Spindle is given on the first visit (CLEAR_SPINDLE and
/// CLEAR_SPINDLE_ON_SHIP: the group's, as the stage 3b-2 plan has the
/// devices), and the Kohr-Ah's victory sensed after the first year; their
/// friendship (PKUNK_MANNER 3: never an alliance, pkunkc.c never calls
/// SetRaceAllied, so their ships never go on sale) won by offering help
/// or asking for it, with 4 ships to the escort fleet (the host's, whoever
/// answers: AddEscortShips, as much as it has room for) and their
/// migration's event queued (ADVANCE_PKUNK_MISSION, set aside until stage
/// 3c); then once a year, on or after the date a year from the last gift
/// (PKUNK_SHIP_MONTH, _DAY, _YEAR), more ships at the homeworld (ShipsReady,
/// PrepareShip), or no room. Their anger after a fight (post_pkunk_enc:
/// PKUNK_MANNER 1), mended at home with the spindle if it wasn't given;
/// their hatred for good after "we must conquer" (PKUNK_MANNER 2); the
/// migration's reasons (PKUNK_REASONS: the good ones queue the event at
/// once, set aside too). Every path is checked against UQM's own script
/// (tests/golden/talks-pkunk.json).
/// </summary>
public sealed class PkunkScript : CommScript
{
    // races.h's PKUNK_SHIP.
    private const int PkunkShip = 4;

    // MAX_PKUNK_SHIPS: the ships each gift gives.
    private const int MaxPkunkShips = 4;

    // START_YEAR (clock.h).
    private const int StartYear = 2155;

    // PKUNK_REASONS' bits.
    private const int GoodReason1 = 1 << 0, GoodReason2 = 1 << 1, BadReason1 = 1 << 2, BadReason2 = 1 << 3;

    public PkunkScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    // ShipsReady: their friends, on or after the next ships' date (the
    // year counted from START_YEAR, then the month, then the day).
    private bool ShipsReady()
    {
        int i;
        return GetGameState("PKUNK_MANNER") == 3
               && !((i = (short)((T.Clock.Year - StartYear) - GetGameState("PKUNK_SHIP_YEAR"))) < 0
                    || ((i == 0 && (i = (short)(T.Clock.Month - GetGameState("PKUNK_SHIP_MONTH"))) < 0)
                        || (i == 0 && T.Clock.Day < GetGameState("PKUNK_SHIP_DAY"))));
    }

    // PrepareShip: 4 ships to the escort fleet (the host's), and if any
    // joined, the next ships' date a year on (its day at most the 28th).
    private void PrepareShip()
    {
        if (T.AddEscortShips(PkunkShip, MaxPkunkShips) != 0)
        {
            int mi = T.Clock.Month;
            SetGameState("PKUNK_SHIP_MONTH", mi);
            int di = T.Clock.Day;
            if (di > 28)
                di = 28;
            SetGameState("PKUNK_SHIP_DAY", di);
            int yi = (byte)((byte)(T.Clock.Year - StartYear) + 1);
            SetGameState("PKUNK_SHIP_YEAR", yi);
        }
    }

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Peace);

        if (PlayerSaid(r, "friendly_bye_space"))
            NPCPhrase("FRIENDLY_GOODBYE_SPACE");
        else if (PlayerSaid(r, "neutral_bye_space"))
            NPCPhrase("NEUTRAL_GOODBYE_SPACE");
        else if (PlayerSaid(r, "bye_angry"))
            NPCPhrase("GOODBYE_ANGRY");
        else if (PlayerSaid(r, "bye_friendly"))
            NPCPhrase("GOODBYE_FRIENDLY");
        else if (PlayerSaid(r, "we_here_to_help") || PlayerSaid(r, "we_need_help"))
        {
            if (PlayerSaid(r, "we_here_to_help"))
                NPCPhrase("NEED_HELP");
            else
                NPCPhrase("GIVE_HELP");
            NPCPhrase("ALMOST_ALLIANCE");

            SetGameState("PKUNK_MANNER", 3);
            SetGameState("PKUNK_VISITS", 0);
            SetGameState("PKUNK_HOME_VISITS", 0);
            SetGameState("PKUNK_INFO", 0);

            T.AddEvent(true, 6, 0, 0, "ADVANCE_PKUNK_MISSION");
            if (Sis.EscortRoom == 0)   // EscortFeasibilityStudy (PKUNK_SHIP)
                NPCPhrase("INIT_NO_ROOM");
            else
            {
                NPCPhrase("INIT_SHIP_GIFT");
                T.AlienTalkSegue(Talk.WaitAll);
                PrepareShip();
            }
        }
        else if (PlayerSaid(r, "try_to_be_nicer"))
        {
            NPCPhrase("CANT_ASK_FOR_MORE");
            NPCPhrase("VISIT_OUR_HOMEWORLD");

            SetGameState("PKUNK_MANNER", 3);
            SetGameState("PKUNK_VISITS", 0);
            SetGameState("PKUNK_HOME_VISITS", 0);
            SetGameState("PKUNK_INFO", 0);
        }
        else if (PlayerSaid(r, "must_conquer") || PlayerSaid(r, "obey"))
        {
            if (PlayerSaid(r, "obey"))
                NPCPhrase("NO_OBEY");
            else
            {
                NPCPhrase("BAD_IDEA");

                SetGameState("PKUNK_MANNER", 2);
            }
            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "die_idiot_fools"))
        {
            NPCPhrase("VERY_WELL");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "suit_yourself"))
            NPCPhrase("GOODBYE_MIGRATION");
        else
        {
            int reasonMask = GetGameState("PKUNK_REASONS");
            if (PlayerSaid(r, "good_reason_1"))
            {
                NPCPhrase("WE_GO_HOME_1");
                reasonMask |= GoodReason1;
                T.AddEvent(true, 0, 0, 0, "ADVANCE_PKUNK_MISSION");
            }
            else if (PlayerSaid(r, "good_reason_2"))
            {
                NPCPhrase("WE_GO_HOME_2");
                reasonMask |= GoodReason2;
                T.AddEvent(true, 0, 0, 0, "ADVANCE_PKUNK_MISSION");
            }
            else if (PlayerSaid(r, "bad_reason_1"))
            {
                NPCPhrase("NO_GO_HOME_1");
                reasonMask |= BadReason1;
            }
            else if (PlayerSaid(r, "bad_reason_2"))
            {
                NPCPhrase("NO_GO_HOME_2");
                reasonMask |= BadReason2;
            }
            SetGameState("PKUNK_REASONS", reasonMask);
        }
    }

    private void PkunkAngry(int r)
    {
        if (PlayerSaid(r, "why_insults"))
        {
            NPCPhrase("RELEASE_TENSION");

            DisablePhrase("why_insults");
        }
        else if (PlayerSaid(r, "what_about_you"))
        {
            NPCPhrase("ABOUT_US");

            DisablePhrase("what_about_you");
        }
        else if (PlayerSaid(r, "should_be_friends"))
        {
            NPCPhrase("YES_FRIENDS");

            DisablePhrase("should_be_friends");
        }

        if (PhraseEnabled("should_be_friends"))
            Response("should_be_friends", PkunkAngry);
        else
        {
            if ((GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0)
                Response("try_to_be_nicer", PkunkHome);
            else
                Response("try_to_be_nicer", ExitConversation);
        }
        Response("die_idiot_fools", ExitConversation);
        if (PhraseEnabled("why_insults"))
            Response("why_insults", PkunkAngry);
        if (PhraseEnabled("what_about_you"))
            Response("what_about_you", PkunkAngry);
        Response("bye_angry", ExitConversation);
    }

    private void DiscussConquer(int r)
    {
        if (PlayerSaid(r, "we_conquer"))
        {
            NPCPhrase("WHY_CONQUER");

            DisablePhrase("we_conquer");
        }
        else if (PlayerSaid(r, "conquer_because_1"))
        {
            // UQM's #if 0: the alliance's name spliced in, left out.
            NPCPhrase("NOT_CONQUER_1");

            DisablePhrase("conquer_because_1");
        }
        else if (PlayerSaid(r, "conquer_because_2"))
        {
            NPCPhrase("NOT_CONQUER_2");

            DisablePhrase("conquer_because_2");
        }

        if (PhraseEnabled("conquer_because_1"))
            Response("conquer_because_1", DiscussConquer);
        if (PhraseEnabled("conquer_because_2"))
            Response("conquer_because_2", DiscussConquer);
        Response("must_conquer", ExitConversation);
        Response("no_conquest", PkunkHome);
    }

    private void OfferAlliance(int r)
    {
        if (PlayerSaid(r, "we_are_vindicator0"))
            NPCPhrase("WHY_YOU_HERE");
        else if (PlayerSaid(r, "exploring_universe"))
        {
            NPCPhrase("SENSE_DEEPER_CONFLICT");

            DisablePhrase("exploring_universe");
        }
        else if (PlayerSaid(r, "fun_cruise"))
        {
            NPCPhrase("REPRESS");

            DisablePhrase("fun_cruise");
        }

        Response("we_here_to_help", ExitConversation);
        Response("we_need_help", ExitConversation);
        if (PhraseEnabled("exploring_universe"))
            Response("exploring_universe", OfferAlliance);
        if (PhraseEnabled("fun_cruise"))
            Response("fun_cruise", OfferAlliance);
    }

    private void AboutPkunk(int r)
    {
        bool infoLeft = false;
        if (PlayerSaid(r, "what_about_you"))
            NPCPhrase("ABOUT_US");
        else if (PlayerSaid(r, "what_about_history"))
        {
            NPCPhrase("ABOUT_HISTORY");

            DisablePhrase("what_about_history");
        }
        else if (PlayerSaid(r, "what_about_yehat"))
        {
            NPCPhrase("ABOUT_YEHAT");

            DisablePhrase("what_about_yehat");
        }
        else if (PlayerSaid(r, "what_about_culture"))
        {
            NPCPhrase("ABOUT_CULTURE");

            DisablePhrase("what_about_culture");
        }
        else if (PlayerSaid(r, "elaborate_culture"))
        {
            NPCPhrase("OK_ELABORATE_CULTURE");

            DisablePhrase("elaborate_culture");
        }
        else if (PlayerSaid(r, "what_about_future"))
        {
            NPCPhrase("ABOUT_FUTURE");

            DisablePhrase("what_about_future");
        }

        if (PhraseEnabled("what_about_history"))
        {
            Response("what_about_history", AboutPkunk);
            infoLeft = true;
        }
        else if (PhraseEnabled("what_about_yehat"))
        {
            Response("what_about_yehat", AboutPkunk);
            infoLeft = true;
        }
        if (PhraseEnabled("what_about_culture"))
        {
            Response("what_about_culture", AboutPkunk);
            infoLeft = true;
        }
        else if (PhraseEnabled("elaborate_culture"))
        {
            Response("elaborate_culture", AboutPkunk);
            infoLeft = true;
        }
        if (PhraseEnabled("what_about_future"))
        {
            Response("what_about_future", AboutPkunk);
            infoLeft = true;
        }
        Response("enough_about_you", PkunkHome);

        if (!infoLeft)
            DisablePhrase("what_about_you");
    }

    private void AboutIlwrath(int r)
    {
        bool infoLeft = false;
        if (PlayerSaid(r, "what_about_ilwrath"))
            NPCPhrase("ABOUT_ILWRATH");
        else if (PlayerSaid(r, "why_ilwrath_fight"))
        {
            NPCPhrase("ILWRATH_FIGHT_BECAUSE");

            DisablePhrase("why_ilwrath_fight");
        }
        else if (PlayerSaid(r, "when_fight_start"))
        {
            NPCPhrase("FIGHT_START_WHEN");

            DisablePhrase("when_fight_start");
        }
        else if (PlayerSaid(r, "how_goes_fight"))
        {
            NPCPhrase("FIGHT_GOES");

            DisablePhrase("how_goes_fight");
        }
        else if (PlayerSaid(r, "how_stop_fight"))
        {
            NPCPhrase("STOP_FIGHT_LIKE_SO");

            DisablePhrase("how_stop_fight");
        }

        foreach (string phrase in new[] { "why_ilwrath_fight", "when_fight_start", "how_goes_fight", "how_stop_fight" })
        {
            if (PhraseEnabled(phrase))
            {
                Response(phrase, AboutIlwrath);
                infoLeft = true;
            }
        }
        Response("enough_ilwrath", PkunkHome);

        if (!infoLeft)
            DisablePhrase("what_about_ilwrath");
    }

    private void PkunkHome(int r)
    {
        int numVisits;

        if (PlayerSaid(r, "no_conquest"))
            NPCPhrase("GOOD_IDEA");
        else if (PlayerSaid(r, "enough_ilwrath"))
            NPCPhrase("OK_ENOUGH_ILWRATH");
        else if (PlayerSaid(r, "enough_about_you"))
            NPCPhrase("OK_ENOUGH_ABOUT_US");
        else if (PlayerSaid(r, "where_fleet_1") || PlayerSaid(r, "where_fleet_2") || PlayerSaid(r, "where_fleet_3"))
        {
            SetGameState("PKUNK_SWITCH", 1);
            if ((GetGameState("PKUNK_MISSION") & 1) == 0)
            {
                numVisits = GetGameState("PKUNK_RETURN");
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("RETURNING_FROM_YEHAT_1");
                        break;
                    case 1:
                        NPCPhrase("RETURNING_FROM_YEHAT_2");
                        --numVisits;
                        break;
                }
                SetGameState("PKUNK_RETURN", numVisits);
            }
            else
            {
                numVisits = GetGameState("PKUNK_MIGRATE");
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("MIGRATING_HOMEWORLD_1");
                        break;
                    case 1:
                        NPCPhrase("MIGRATING_HOMEWORLD_2");
                        break;
                    case 2:
                        NPCPhrase("MIGRATING_HOMEWORLD_3");
                        --numVisits;
                        break;
                }
                SetGameState("PKUNK_MIGRATE", numVisits);
            }

            numVisits = GetGameState("PKUNK_FLEET") + 1;
            SetGameState("PKUNK_FLEET", numVisits);

            DisablePhrase("where_fleet_1");
        }
        else if (PlayerSaid(r, "am_worried_1") || PlayerSaid(r, "am_worried_2") || PlayerSaid(r, "am_worried_3"))
        {
            numVisits = GetGameState("PKUNK_WORRY");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("DONT_WORRY_1");
                    break;
                case 1:
                    NPCPhrase("DONT_WORRY_2");
                    break;
                case 2:
                    NPCPhrase("DONT_WORRY_3");
                    --numVisits;
                    break;
            }
            SetGameState("PKUNK_WORRY", numVisits);

            DisablePhrase("am_worried_1");
        }
        else if (PlayerSaid(r, "try_to_be_nicer"))
        {
            NPCPhrase("CANT_ASK_FOR_MORE");
            if (GetGameState("CLEAR_SPINDLE") == 0)
            {
                NPCPhrase("GIVE_SPINDLE");

                SetGameState("CLEAR_SPINDLE", 1);
                SetGameState("CLEAR_SPINDLE_ON_SHIP", 1);
            }
            NPCPhrase("CAN_BE_FRIENDS");

            SetGameState("PKUNK_MANNER", 3);
            SetGameState("PKUNK_VISITS", 0);
            SetGameState("PKUNK_HOME_VISITS", 0);
        }
        else if (PlayerSaid(r, "what_about_ilwrath"))
        {
            NPCPhrase("ABOUT_ILWRATH" /* ILWRATH_GONE */);

            DisablePhrase("what_about_ilwrath");
        }

        if (PhraseEnabled("we_conquer") && GetGameState("PKUNK_MANNER") == 0)
            Response("we_conquer", DiscussConquer);
        if (GetGameState("PKUNK_ON_THE_MOVE") != 0)
        {
            if (PhraseEnabled("where_fleet_1") && GetGameState("PKUNK_SWITCH") == 0)
            {
                switch (GetGameState("PKUNK_FLEET"))
                {
                    case 0:
                        Response("where_fleet_1", PkunkHome);
                        break;
                    case 1:
                        Response("where_fleet_2", PkunkHome);
                        break;
                    case 2:
                        Response("where_fleet_3", PkunkHome);
                        break;
                }
            }
            else if (!PhraseEnabled("where_fleet_1") && PhraseEnabled("am_worried_1")
                     && (GetGameState("PKUNK_MISSION") & 1) != 0)
            {
                switch (GetGameState("PKUNK_WORRY"))
                {
                    case 0:
                        Response("am_worried_1", PkunkHome);
                        break;
                    case 1:
                        Response("am_worried_2", PkunkHome);
                        break;
                    case 2:
                        Response("am_worried_3", PkunkHome);
                        break;
                }
            }
        }
        if (GetGameState("PKUNK_SHIP_MONTH") == 0)
        {
            T.ConstructResponse("we_are_vindicator0", Sis.CaptainName, "we_are_vindicator1",
                Sis.ShipName, "we_are_vindicator2");
            T.DoResponsePhrase("we_are_vindicator0", OfferAlliance, Talk.SharedPhraseBuffer);
        }
        if (PhraseEnabled("what_about_you"))
            Response("what_about_you", AboutPkunk);
        if (PhraseEnabled("what_about_ilwrath"))
        {
            if (GetGameState("ILWRATH_DECEIVED") == 0)
                Response("what_about_ilwrath", AboutIlwrath);
            else
                Response("what_about_ilwrath", PkunkHome);
        }
        Response("bye_friendly", ExitConversation);
    }

    private void PkunkFriendlySpace(int r)
    {
        int numVisits;

        if (PlayerSaid(r, "whats_up_space"))
        {
            if (ShipsReady())
                NPCPhrase("SHIPS_AT_HOME");
            else
            {
                numVisits = GetGameState("PKUNK_INFO");
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
                        break;
                    case 5:
                        NPCPhrase("GENERAL_INFO_SPACE_6");
                        break;
                    case 6:
                        NPCPhrase("GENERAL_INFO_SPACE_7");
                        break;
                    case 7:
                        NPCPhrase("GENERAL_INFO_SPACE_8");
                        --numVisits;
                        break;
                }
                SetGameState("PKUNK_INFO", numVisits);
            }

            DisablePhrase("whats_up_space");
        }
        else if (PlayerSaid(r, "how_goes_war"))
        {
            numVisits = GetGameState("PKUNK_WAR");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("WAR_GOES_1");
                    SetGameState("KNOW_URQUAN_STORY", 1);
                    SetGameState("KNOW_KOHR_AH_STORY", 1);
                    break;
                case 1:
                    NPCPhrase("WAR_GOES_2");
                    break;
                case 2:
                    NPCPhrase("WAR_GOES_3");
                    break;
                case 3:
                    NPCPhrase("WAR_GOES_4");
                    SetGameState("PKUNK_DONE_WAR", 1);
                    --numVisits;
                    break;
            }
            SetGameState("PKUNK_WAR", numVisits);

            DisablePhrase("how_goes_war");
        }
        else if (PlayerSaid(r, "tell_my_fortune"))
        {
            numVisits = GetGameState("PKUNK_FORTUNE");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("FORTUNE_IS_1");
                    break;
                case 1:
                    NPCPhrase("FORTUNE_IS_2");
                    break;
                case 2:
                    NPCPhrase("FORTUNE_IS_3");
                    break;
                case 3:
                    NPCPhrase("FORTUNE_IS_4");
                    break;
                case 4:
                    NPCPhrase("FORTUNE_IS_5");
                    break;
                case 5:
                    NPCPhrase("FORTUNE_IS_6");
                    break;
                case 6:
                    NPCPhrase("FORTUNE_IS_7");
                    break;
                case 7:
                    NPCPhrase("FORTUNE_IS_8");
                    --numVisits;
                    break;
            }
            SetGameState("PKUNK_FORTUNE", numVisits);

            DisablePhrase("tell_my_fortune");
        }

        if (PhraseEnabled("whats_up_space"))
            Response("whats_up_space", PkunkFriendlySpace);
        if (GetGameState("PKUNK_DONE_WAR") == 0 && PhraseEnabled("how_goes_war"))
            Response("how_goes_war", PkunkFriendlySpace);
        if (PhraseEnabled("tell_my_fortune"))
            Response("tell_my_fortune", PkunkFriendlySpace);
        Response("friendly_bye_space", ExitConversation);
    }

    private void PkunkNeutralSpace(int r)
    {
        if (PlayerSaid(r, "form_alliance"))
        {
            NPCPhrase("GO_TO_HOMEWORLD");

            DisablePhrase("form_alliance");
        }
        else if (PlayerSaid(r, "can_you_help"))
        {
            NPCPhrase("GO_TO_HOMEWORLD_AGAIN");

            DisablePhrase("can_you_help");
        }
        else if (PlayerSaid(r, "hostile_greeting"))
        {
            NPCPhrase("DONT_BE_HOSTILE");

            DisablePhrase("hostile_greeting");
        }
        else if (PlayerSaid(r, "whats_up_neutral"))
        {
            int numVisits = GetGameState("PKUNK_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_SPACE_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_SPACE_2");
                    break;
                case 2:
                    NPCPhrase("GENERAL_INFO_SPACE_6" /* was 3 */);
                    break;
                case 3:
                    NPCPhrase("GENERAL_INFO_SPACE_7" /* was 4 */);
                    --numVisits;
                    break;
            }
            SetGameState("PKUNK_INFO", numVisits);

            DisablePhrase("whats_up_neutral");
        }

        if (PhraseEnabled("form_alliance"))
            Response("form_alliance", PkunkNeutralSpace);
        else if (PhraseEnabled("can_you_help"))
            Response("can_you_help", PkunkNeutralSpace);
        if (PhraseEnabled("hostile_greeting"))
            Response("hostile_greeting", PkunkNeutralSpace);
        else
            Response("obey", ExitConversation);
        if (PhraseEnabled("whats_up_neutral"))
            Response("whats_up_neutral", PkunkNeutralSpace);
        Response("neutral_bye_space", ExitConversation);
    }

    private void PkunkMigrate(int r)
    {
        int reasonMask = GetGameState("PKUNK_REASONS");
        if ((reasonMask & GoodReason1) == 0)
            Response("good_reason_1", ExitConversation);
        if ((reasonMask & BadReason1) == 0)
            Response("bad_reason_1", ExitConversation);
        if ((reasonMask & GoodReason2) == 0)
            Response("good_reason_2", ExitConversation);
        if ((reasonMask & BadReason2) == 0)
            Response("bad_reason_2", ExitConversation);
        Response("suit_yourself", ExitConversation);
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

        int manner = GetGameState("PKUNK_MANNER");
        if (manner == 2)
        {
            // Irreparably pissed off the Pkunk.
            numVisits = GetGameState("PKUNK_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("HATE_YOU_FOREVER_1");
                    break;
                case 1:
                    NPCPhrase("HATE_YOU_FOREVER_2");
                    break;
                case 2:
                    NPCPhrase("HATE_YOU_FOREVER_3");
                    break;
                case 3:
                    NPCPhrase("HATE_YOU_FOREVER_4");
                    --numVisits;
                    break;
            }
            SetGameState("PKUNK_VISITS", numVisits);

            T.SetSegue(Segue.Hostile);
        }
        else if (manner == 1)
        {
            // Bad relations with the Pkunk, but not irreparably.
            numVisits = GetGameState("PKUNK_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("SPIRITUAL_PROBLEMS_1");
                    break;
                case 1:
                    NPCPhrase("SPIRITUAL_PROBLEMS_2");
                    break;
                case 2:
                    NPCPhrase("SPIRITUAL_PROBLEMS_3");
                    break;
                case 3:
                    NPCPhrase("SPIRITUAL_PROBLEMS_4");
                    --numVisits;
                    break;
            }
            SetGameState("PKUNK_VISITS", numVisits);

            PkunkAngry(0);
        }
        else if ((GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0)
        {
            // Encountering the Pkunk at their home world.
            if (GetGameState("CLEAR_SPINDLE") == 0)
            {
                NPCPhrase("GIVE_SPINDLE");

                SetGameState("CLEAR_SPINDLE", 1);
                SetGameState("CLEAR_SPINDLE_ON_SHIP", 1);
            }
            else if (GetGameState("PKUNK_SENSE_VICTOR") == 0 && T.Clock.Year > StartYear
                     && GetGameState("KOHR_AH_FRENZY") == 0)
            {
                NPCPhrase("SENSE_KOHRAH_VICTORY");

                SetGameState("PKUNK_SENSE_VICTOR", 1);
            }

            numVisits = GetGameState("PKUNK_HOME_VISITS");
            if (manner == 0)
            {
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("NEUTRAL_HOMEWORLD_HELLO_1");
                        break;
                    case 1:
                        NPCPhrase("NEUTRAL_HOMEWORLD_HELLO_2");
                        break;
                    case 2:
                        NPCPhrase("NEUTRAL_HOMEWORLD_HELLO_3");
                        break;
                    case 3:
                        NPCPhrase("NEUTRAL_HOMEWORLD_HELLO_4");
                        --numVisits;
                        break;
                }
            }
            else
            {
                if (numVisits != 0 && ShipsReady())
                {
                    // The yearly gift: more ships, if the escort fleet (the host's) has room.
                    if (Sis.EscortRoom == 0)   // EscortFeasibilityStudy (PKUNK_SHIP)
                        NPCPhrase("NO_ROOM");
                    else
                    {
                        NPCPhrase("SHIP_GIFT");
                        PrepareShip();
                    }
                }
                else
                {
                    switch (numVisits++)
                    {
                        case 0:
                            NPCPhrase("FRIENDLY_HOMEWORLD_HELLO_1");
                            break;
                        case 1:
                            NPCPhrase("FRIENDLY_HOMEWORLD_HELLO_2");
                            break;
                        case 2:
                            NPCPhrase("FRIENDLY_HOMEWORLD_HELLO_3");
                            break;
                        case 3:
                            NPCPhrase("FRIENDLY_HOMEWORLD_HELLO_4");
                            break;
                        case 4:
                            NPCPhrase("FRIENDLY_HOMEWORLD_HELLO_5");
                            break;
                        case 5:
                            NPCPhrase("FRIENDLY_HOMEWORLD_HELLO_6");
                            break;
                        case 6:
                            NPCPhrase("FRIENDLY_HOMEWORLD_HELLO_7");
                            break;
                        case 7:
                            NPCPhrase("FRIENDLY_HOMEWORLD_HELLO_8");
                            --numVisits;
                            break;
                    }
                }
            }
            SetGameState("PKUNK_HOME_VISITS", numVisits);

            PkunkHome(0);
        }
        else if ((numVisits = GetGameState("PKUNK_MISSION")) == 0 || (numVisits & 1) == 0)
        {
            // Encountering a Pkunk ship in space, while they are not migrating.
            numVisits = GetGameState("PKUNK_VISITS");
            if (manner == 3)
            {
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("FRIENDLY_SPACE_HELLO_1");
                        break;
                    case 1:
                        NPCPhrase("FRIENDLY_SPACE_HELLO_2");
                        break;
                    case 2:
                        NPCPhrase("FRIENDLY_SPACE_HELLO_3");
                        break;
                    case 3:
                        NPCPhrase("FRIENDLY_SPACE_HELLO_4");
                        break;
                    case 4:
                        NPCPhrase("FRIENDLY_SPACE_HELLO_5");
                        break;
                    case 5:
                        NPCPhrase("FRIENDLY_SPACE_HELLO_6");
                        break;
                    case 6:
                        NPCPhrase("FRIENDLY_SPACE_HELLO_7");
                        break;
                    case 7:
                        NPCPhrase("FRIENDLY_SPACE_HELLO_8");
                        --numVisits;
                        break;
                }

                PkunkFriendlySpace(0);
            }
            else
            {
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("NEUTRAL_SPACE_HELLO_1");
                        break;
                    case 1:
                        NPCPhrase("NEUTRAL_SPACE_HELLO_2");
                        break;
                    case 2:
                        NPCPhrase("NEUTRAL_SPACE_HELLO_3");
                        break;
                    case 3:
                        NPCPhrase("NEUTRAL_SPACE_HELLO_4");
                        --numVisits;
                        break;
                }

                PkunkNeutralSpace(0);
            }
            SetGameState("PKUNK_VISITS", numVisits);
        }
        else
        {
            // Encountering a Pkunk ship in space, while they are migrating.
            numVisits = GetGameState("PKUNK_MIGRATE_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("MIGRATING_SPACE_1");
                    break;
                case 1:
                    NPCPhrase("MIGRATING_SPACE_2");
                    break;
                case 2:
                    NPCPhrase("MIGRATING_SPACE_3");
                    break;
                case 3:
                    NPCPhrase("MIGRATING_SPACE_4");
                    break;
                case 4:
                    NPCPhrase("MIGRATING_SPACE_5");
                    break;
                case 5:
                    NPCPhrase("MIGRATING_SPACE_6");
                    break;
                case 6:
                    NPCPhrase("MIGRATING_SPACE_7");
                    break;
                case 7:
                    NPCPhrase("MIGRATING_SPACE_8");
                    --numVisits;
                    break;
            }
            SetGameState("PKUNK_MIGRATE_VISITS", numVisits);

            PkunkMigrate(0);
        }
    }

    // post_pkunk_enc: a hostile ending (a fight, or a hostile reply) angers
    // them (PKUNK_MANNER 1, their greetings counted afresh) unless they
    // already hate the players for good.
    protected internal override void PostEncounter()
    {
        int manner;
        if (T.Segue == Segue.Hostile && (manner = GetGameState("PKUNK_MANNER")) != 2)
        {
            SetGameState("PKUNK_MANNER", 1);
            if (manner != 1)
            {
                SetGameState("PKUNK_VISITS", 0);
                SetGameState("PKUNK_HOME_VISITS", 0);
            }
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if (GetGameState("PKUNK_MANNER") == 3 || T.Activity == TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Peace);   // enter communications immediately
        else
            T.SetSegue(Segue.Hostile);   // ask the player whether to attack or talk
    }
}
