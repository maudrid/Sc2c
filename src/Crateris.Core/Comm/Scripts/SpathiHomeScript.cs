// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/spahome/spahome.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Spathi at home (spahome.c), whole: the Safe Ones' council on
/// Spathiwa's moon, which genspa.c's orbit starts with
/// GLOBAL_FLAGS_AND_DATA bit 7 against their infinite fleet. The password
/// asked (known from the Melnorme, KNOW_SPATHI_PASSWORD, or from Fwiffo,
/// FOUND_PLUTO_SPATHI), the council and the alliance offered, the quest
/// (the Evil Ones on Spathiwa, SPATHI_CREATURES_ELIMINATED, which the
/// planet's pickups set: <see cref="Story.StoryWorlds.PickupLife"/>), the
/// party and the alliance made (SetRaceAllied; the shield event it queues,
/// SPATHI_SHIELD_EVENT, run six months on since stage 3c item 1), the allies' talk, and
/// their anger after a fight or a lie. Fwiffo in the escorts (HaveEscortShip,
/// the host's fleet: the stage 3b-2 plan's question 4) is a hostage. Every
/// path is checked against UQM's own script (tests/golden/talks-spahome.json).
/// </summary>
public sealed class SpathiHomeScript : CommScript
{
    // races.h's SPATHI_SHIP.
    private const int SpathiShip = 6;

    public SpathiHomeScript(ConversationScript phrases) : base(phrases)
    {
    }

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Peace);
        if (PlayerSaid(r, "we_attack_again"))
        {
            NPCPhrase("WE_FIGHT_AGAIN");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "surrender_or_die"))
        {
            NPCPhrase("DEFEND_OURSELVES");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "we_are_vindicator0"))
        {
            NPCPhrase("NO_PASSWORD");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "gort_merenga") || PlayerSaid(r, "guph_florp") || PlayerSaid(r, "wagngl_fthagn")
                 || PlayerSaid(r, "pleeese"))
        {
            NPCPhrase("WRONG_PASSWORD");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "screw_password"))
        {
            NPCPhrase("NO_PASSWORD");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "bye_no_ally_offer"))
            NPCPhrase("GOODBYE_NO_ALLY_OFFER");
        else if (PlayerSaid(r, "bye_angry_spathi"))
            NPCPhrase("GOODBYE_ANGRY_SPATHI");
        else if (PlayerSaid(r, "bye_ally"))
            NPCPhrase("GOODBYE_ALLY");
        else if (PlayerSaid(r, "already_got_them"))
        {
            NPCPhrase("EARLY_BIRD_CHECK");

            SetGameState("SPATHI_HOME_VISITS", 0);
            SetGameState("SPATHI_VISITS", 0);
            SetGameState("SPATHI_PARTY", 1);
            SetGameState("SPATHI_MANNER", 3);
        }
        else if (PlayerSaid(r, "too_dangerous"))
            NPCPhrase("WE_AGREE");
        else if (PlayerSaid(r, "think_more"))
            NPCPhrase("COWARD");
        else if (PlayerSaid(r, "i_accept"))
        {
            NPCPhrase("AWAIT_RETURN");

            SetGameState("SPATHI_QUEST", 1);
            SetGameState("SPATHI_MANNER", 3);
            SetGameState("SPATHI_VISITS", 0);
        }
        else if (PlayerSaid(r, "do_as_we_say"))
        {
            NPCPhrase("DEPART_FOR_EARTH");

            T.SetRaceAllied(SpathiShip, true);
            // The shield event, six months on.
            T.AddEvent(relative: true, 6, 0, 0, "SPATHI_SHIELD_EVENT");
            SetGameState("SPATHI_HOME_VISITS", 0);
            SetGameState("SPATHI_VISITS", 0);
        }
        else if (PlayerSaid(r, "killed_them_all_1"))
        {
            NPCPhrase("WILL_CHECK_1");

            if (GetGameState("SPATHI_CREATURES_ELIMINATED") == 0)
                SetGameState("LIED_ABOUT_CREATURES", 1);
            else
            {
                SetGameState("SPATHI_HOME_VISITS", 0);
                SetGameState("SPATHI_VISITS", 0);
                SetGameState("SPATHI_PARTY", 1);
                SetGameState("SPATHI_MANNER", 3);
            }
        }
        else if (PlayerSaid(r, "killed_them_all_2"))
        {
            NPCPhrase("WILL_CHECK_2");

            if (GetGameState("SPATHI_CREATURES_ELIMINATED") == 0)
                SetGameState("LIED_ABOUT_CREATURES", 2);
            else
            {
                SetGameState("SPATHI_HOME_VISITS", 0);
                SetGameState("SPATHI_VISITS", 0);
                SetGameState("SPATHI_PARTY", 1);
                SetGameState("SPATHI_MANNER", 3);
            }
        }
        else if (PlayerSaid(r, "bye_before_party"))
            NPCPhrase("GOODBYE_BEFORE_PARTY");
        else if (PlayerSaid(r, "bye_from_party_1") || PlayerSaid(r, "bye_from_party_2")
                 || PlayerSaid(r, "bye_from_party_3"))
            NPCPhrase("GOODBYE_FROM_PARTY");
    }

    private void SpathiInfo(int r)
    {
        bool infoLeft = false;
        if (PlayerSaid(r, "like_some_info"))
            NPCPhrase("WHAT_ABOUT");
        else if (PlayerSaid(r, "what_about_hierarchy"))
        {
            NPCPhrase("ABOUT_HIERARCHY");

            DisablePhrase("what_about_hierarchy");
        }
        else if (PlayerSaid(r, "what_about_history"))
        {
            NPCPhrase("ABOUT_HISTORY");

            DisablePhrase("what_about_history");
        }
        else if (PlayerSaid(r, "what_about_alliance"))
        {
            NPCPhrase("ABOUT_ALLIANCE");

            DisablePhrase("what_about_alliance");
        }
        else if (PlayerSaid(r, "what_about_other"))
        {
            NPCPhrase("ABOUT_OTHER");

            DisablePhrase("what_about_other");
        }
        else if (PlayerSaid(r, "what_about_precursors"))
        {
            NPCPhrase("ABOUT_PRECURSORS");

            DisablePhrase("what_about_precursors");
        }

        if (PhraseEnabled("what_about_hierarchy"))
        {
            infoLeft = true;
            Response("what_about_hierarchy", SpathiInfo);
        }
        if (PhraseEnabled("what_about_history"))
        {
            infoLeft = true;
            Response("what_about_history", SpathiInfo);
        }
        if (PhraseEnabled("what_about_alliance"))
        {
            infoLeft = true;
            Response("what_about_alliance", SpathiInfo);
        }
        if (PhraseEnabled("what_about_other"))
        {
            infoLeft = true;
            Response("what_about_other", SpathiInfo);
        }
        if (PhraseEnabled("what_about_precursors"))
        {
            infoLeft = true;
            Response("what_about_precursors", SpathiInfo);
        }
        Response("enough_info", SpathiAllies);

        if (!infoLeft)
            DisablePhrase("like_some_info");
    }

    private void SpathiAllies(int r)
    {
        int numVisits;

        if (r == 0)
        {
            numVisits = GetGameState("SPATHI_HOME_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("HELLO_ALLIES_1");
                    break;
                case 1:
                    NPCPhrase("HELLO_ALLIES_2");
                    break;
                case 2:
                    NPCPhrase("HELLO_ALLIES_3");
                    --numVisits;
                    break;
            }
            SetGameState("SPATHI_HOME_VISITS", numVisits);
        }
        else if (PlayerSaid(r, "whats_up"))
        {
            numVisits = GetGameState("SPATHI_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_2");
                    break;
                case 2:
                    NPCPhrase("GENERAL_INFO_3");
                    SetGameState("KNOW_URQUAN_STORY", 1);
                    SetGameState("KNOW_KOHR_AH_STORY", 1);
                    break;
                case 3:
                    NPCPhrase("GENERAL_INFO_4");
                    break;
                case 4:
                    NPCPhrase("GENERAL_INFO_5");
                    --numVisits;
                    break;
                case 5:
                    NPCPhrase("GENERAL_INFO_5");
                    --numVisits;
                    break;
            }
            SetGameState("SPATHI_INFO", numVisits);

            DisablePhrase("whats_up");
        }
        else if (PlayerSaid(r, "resources_please"))
        {
            NPCPhrase("SORRY_NO_RESOURCES");

            DisablePhrase("resources_please");
        }
        else if (PlayerSaid(r, "something_fishy"))
        {
            NPCPhrase("NOTHING_FISHY");

            SetGameState("SPATHI_INFO", 5);
        }
        else if (PlayerSaid(r, "enough_info"))
            NPCPhrase("OK_ENOUGH_INFO");

        if (GetGameState("SPATHI_INFO") == 4)
            Response("something_fishy", SpathiAllies);
        if (PhraseEnabled("whats_up"))
            Response("whats_up", SpathiAllies);
        if (PhraseEnabled("resources_please"))
            Response("resources_please", SpathiAllies);
        if (PhraseEnabled("like_some_info"))
            Response("like_some_info", SpathiInfo);
        Response("bye_ally", ExitConversation);
    }

    private void SpathiQuest(int r)
    {
        if (r == 0)
        {
            if (GetGameState("LIED_ABOUT_CREATURES") == 0)
                NPCPhrase("HOW_GO_EFFORTS");
            else
                NPCPhrase("YOU_LIED_1");
        }
        else if (PlayerSaid(r, "little_mistake"))
        {
            NPCPhrase("BIG_MISTAKE");

            DisablePhrase("little_mistake");
        }
        else if (PlayerSaid(r, "talk_test"))
        {
            NPCPhrase("TEST_AGAIN");

            DisablePhrase("talk_test");
        }
        else if (PlayerSaid(r, "zapped_a_few"))
        {
            NPCPhrase("YOU_FORTUNATE");

            DisablePhrase("zapped_a_few");
        }

        if (GetGameState("LIED_ABOUT_CREATURES") == 0)
            Response("killed_them_all_1", ExitConversation);
        else
        {
            if (PhraseEnabled("little_mistake"))
                Response("little_mistake", SpathiQuest);
            Response("killed_them_all_2", ExitConversation);
        }
        if (GetGameState("SPATHI_CREATURES_ELIMINATED") == 0)
        {
            if (PhraseEnabled("talk_test"))
                Response("talk_test", SpathiQuest);
            if (PhraseEnabled("zapped_a_few") && GetGameState("SPATHI_CREATURES_EXAMINED") != 0)
                Response("zapped_a_few", SpathiQuest);
            Response("bye_before_party", ExitConversation);
        }
    }

    private void LearnQuest(int r)
    {
        if (r == 0)
        {
            NPCPhrase("QUEST_AGAIN");

            DisablePhrase("what_test");
            if (GetGameState("KNOW_SPATHI_EVIL") != 0)
                DisablePhrase("tell_evil");
        }
        else if (PlayerSaid(r, "how_prove"))
            NPCPhrase("BETTER_IDEA");
        else if (PlayerSaid(r, "what_test"))
        {
            NPCPhrase("WIPE_EVIL");

            SetGameState("KNOW_SPATHI_QUEST", 1);
            DisablePhrase("what_test");
        }
        else if (PlayerSaid(r, "tell_evil"))
        {
            NPCPhrase("BEFORE_ACCEPT");

            SetGameState("KNOW_SPATHI_EVIL", 1);
            DisablePhrase("tell_evil");
            DisablePhrase("prove_strength");
        }
        else if (PlayerSaid(r, "prove_strength"))
        {
            NPCPhrase("YOUR_BEHAVIOR");

            DisablePhrase("prove_strength");
        }
        else if (PlayerSaid(r, "why_dont_you_do_it"))
        {
            NPCPhrase("WE_WONT_BECAUSE");

            DisablePhrase("why_dont_you_do_it");
        }

        if (PhraseEnabled("what_test"))
            Response("what_test", LearnQuest);
        else if (GetGameState("SPATHI_CREATURES_ELIMINATED") != 0)
            Response("already_got_them", ExitConversation);
        else if (PhraseEnabled("tell_evil"))
        {
            Response("too_dangerous", ExitConversation);
            Response("tell_evil", LearnQuest);
        }
        else
        {
            Response("too_dangerous", ExitConversation);
            Response("think_more", ExitConversation);
            Response("i_accept", ExitConversation);
        }
        if (PhraseEnabled("prove_strength") && GetGameState("KNOW_SPATHI_QUEST") == 0)
            Response("prove_strength", LearnQuest);
        if (PhraseEnabled("why_dont_you_do_it"))
            Response("why_dont_you_do_it", LearnQuest);
    }

    private void AllianceOffer(int r)
    {
        if (PlayerSaid(r, "misunderstanding"))
        {
            NPCPhrase("JUST_MISUNDERSTANDING");
            T.XFormColorMap(1, Talk.OneSecond / 4);

            SetGameState("SPATHI_MANNER", 3);
            SetGameState("SPATHI_VISITS", 0);
        }
        else if (PlayerSaid(r, "we_come_in_peace"))
            NPCPhrase("OF_COURSE");
        else if (PlayerSaid(r, "hand_in_friendship"))
        {
            NPCPhrase("TOO_AFRAID");

            DisablePhrase("hand_in_friendship");
        }
        else if (PlayerSaid(r, "stronger"))
        {
            NPCPhrase("YOURE_NOT");

            DisablePhrase("stronger");
        }
        else if (PlayerSaid(r, "yes_we_are"))
        {
            NPCPhrase("NO_YOURE_NOT");

            DisablePhrase("yes_we_are");
        }
        else if (PlayerSaid(r, "share_info"))
        {
            NPCPhrase("NO_INFO");

            DisablePhrase("share_info");
        }
        else if (PlayerSaid(r, "give_us_resources"))
        {
            NPCPhrase("NO_RESOURCES");

            DisablePhrase("give_us_resources");
        }

        if (PhraseEnabled("hand_in_friendship"))
            Response("hand_in_friendship", AllianceOffer);
        else if (PhraseEnabled("stronger"))
            Response("stronger", AllianceOffer);
        else if (PhraseEnabled("yes_we_are"))
            Response("yes_we_are", AllianceOffer);
        else
            Response("how_prove", LearnQuest);
        if (PhraseEnabled("share_info"))
            Response("share_info", AllianceOffer);
        if (PhraseEnabled("give_us_resources"))
            Response("give_us_resources", AllianceOffer);
    }

    private void SpathiAngry(int r)
    {
        if (r == 0)
        {
            NPCPhrase("MEAN_GUYS_RETURN");

            Response("we_apologize", SpathiAngry);
        }
        else /* if (R == we_apologize) */
        {
            NPCPhrase("DONT_BELIEVE");

            Response("misunderstanding", AllianceOffer);
        }

        Response("we_attack_again", ExitConversation);
        Response("bye_angry_spathi", ExitConversation);
    }

    private void SpathiParty(int r)
    {
        if (r == 0)
        {
            int numVisits = GetGameState("SPATHI_HOME_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("MUST_PARTY_1");
                    break;
                case 1:
                    NPCPhrase("MUST_PARTY_2");
                    break;
                case 2:
                    NPCPhrase("MUST_PARTY_3");
                    --numVisits;
                    break;
            }
            SetGameState("SPATHI_HOME_VISITS", numVisits);
        }
        else if (PlayerSaid(r, "deals_a_deal"))
        {
            NPCPhrase("WAIT_A_WHILE");

            DisablePhrase("deals_a_deal");
        }
        else if (PlayerSaid(r, "how_long"))
        {
            NPCPhrase("TEN_YEARS");

            DisablePhrase("how_long");
        }
        else if (PlayerSaid(r, "reneging"))
        {
            NPCPhrase("ADULT_VIEW");

            DisablePhrase("reneging");
        }
        else if (PlayerSaid(r, "return_beasts"))
        {
            NPCPhrase("WHAT_RELATIONSHIP");

            DisablePhrase("return_beasts");
        }
        else if (PlayerSaid(r, "minds_and_might"))
        {
            NPCPhrase("HUH");

            DisablePhrase("minds_and_might");
        }
        else if (PlayerSaid(r, "fellowship"))
        {
            NPCPhrase("WHAT");

            DisablePhrase("fellowship");
        }

        if (PhraseEnabled("deals_a_deal"))
            Response("deals_a_deal", SpathiParty);
        else if (PhraseEnabled("how_long"))
            Response("how_long", SpathiParty);
        else if (PhraseEnabled("reneging"))
            Response("reneging", SpathiParty);
        else if (PhraseEnabled("return_beasts"))
            Response("return_beasts", SpathiParty);
        else
        {
            if (PhraseEnabled("minds_and_might"))
                Response("minds_and_might", SpathiParty);
            if (PhraseEnabled("fellowship"))
                Response("fellowship", SpathiParty);
            Response("do_as_we_say", ExitConversation);

            return;
        }
        switch (GetGameState("SPATHI_HOME_VISITS") - 1)
        {
            case 0:
                Response("bye_from_party_1", ExitConversation);
                break;
            case 1:
                Response("bye_from_party_2", ExitConversation);
                break;
            default:
                Response("bye_from_party_3", ExitConversation);
                break;
        }
    }

    private void SpathiCouncil(int r)
    {
        if (r == 0)
            NPCPhrase("HELLO_AGAIN");
        else if (PlayerSaid(r, "good_password"))
        {
            NPCPhrase("YES_GOOD_PASSWORD");
            T.XFormColorMap(1, Talk.OneSecond / 4);

            SetGameState("KNOW_SPATHI_PASSWORD", 1);
            SetGameState("SPATHI_HOME_VISITS", 0);
        }
        else if (PlayerSaid(r, "we_come_in_peace"))
        {
            NPCPhrase("KILLED_SPATHI");

            DisablePhrase("we_come_in_peace");
        }
        else if (PlayerSaid(r, "spathi_on_pluto"))
        {
            NPCPhrase("WHERE_SPATHI");

            DisablePhrase("spathi_on_pluto");
        }
        else if (PlayerSaid(r, "hostage"))
        {
            NPCPhrase("GUN_TO_HEAD");

            SetGameState("FOUND_PLUTO_SPATHI", 3);
            DisablePhrase("hostage");
        }
        else if (PlayerSaid(r, "killed_fwiffo"))
        {
            NPCPhrase("POOR_FWIFFO");

            SetGameState("FOUND_PLUTO_SPATHI", 3);
            DisablePhrase("killed_fwiffo");
        }
        else if (PlayerSaid(r, "fwiffo_fine"))
        {
            NPCPhrase("NOT_LIKELY");

            // (spahome.c sets R = killed_fwiffo here, which nothing after reads.)
            DisablePhrase("fwiffo_fine");
        }
        else if (PlayerSaid(r, "surrender"))
        {
            NPCPhrase("NO_SURRENDER");

            DisablePhrase("surrender");
        }

        if (GetGameState("SPATHI_MANNER") == 0)
            Response("we_come_in_peace", AllianceOffer);
        else if (PhraseEnabled("we_come_in_peace"))
            Response("we_come_in_peace", SpathiCouncil);
        else
            Response("misunderstanding", AllianceOffer);
        if (GetGameState("FOUND_PLUTO_SPATHI") != 0 && GetGameState("FOUND_PLUTO_SPATHI") < 3)
        {
            if (PhraseEnabled("spathi_on_pluto"))
                Response("spathi_on_pluto", SpathiCouncil);
            // Fwiffo in the escorts: the host's fleet, whoever answers (the plan's question 4).
            else if (T.HaveEscortShip(SpathiShip))
            {
                if (PhraseEnabled("hostage"))
                    Response("hostage", SpathiCouncil);
            }
            else if (PhraseEnabled("killed_fwiffo"))
            {
                Response("killed_fwiffo", SpathiCouncil);
                if (PhraseEnabled("fwiffo_fine"))
                    Response("fwiffo_fine", SpathiCouncil);
            }
        }
        if (PhraseEnabled("surrender"))
            Response("surrender", SpathiCouncil);
        else
            Response("surrender_or_die", ExitConversation);
        Response("bye_no_ally_offer", ExitConversation);
    }

    private void SpathiPassword(int r)
    {
        if (r == 0)
        {
            int numVisits = GetGameState("SPATHI_HOME_VISITS");
            switch (numVisits++)
            {
                default:
                    NPCPhrase("WHAT_IS_PASSWORD");
                    numVisits = 1;
                    break;
                case 1:
                    NPCPhrase("WHAT_IS_PASSWORD_AGAIN");
                    --numVisits;
                    break;
            }
            SetGameState("SPATHI_HOME_VISITS", numVisits);
        }
        else if (PlayerSaid(r, "what_do_i_get"))
        {
            NPCPhrase("YOU_GET_TO_LIVE");

            DisablePhrase("what_do_i_get");
        }

        if (GetGameState("FOUND_PLUTO_SPATHI") != 0 || GetGameState("KNOW_SPATHI_PASSWORD") != 0)
        {
            Response("good_password", SpathiCouncil);
            if (PhraseEnabled("what_do_i_get"))
                Response("what_do_i_get", SpathiPassword);
        }
        else
        {
            T.ConstructResponse("we_are_vindicator0", Sis.CaptainName, "we_are_vindicator1",
                Sis.ShipName, "we_are_vindicator2");
            T.DoResponsePhrase("we_are_vindicator0", ExitConversation, Talk.SharedPhraseBuffer);
            Response("gort_merenga", ExitConversation);
            Response("guph_florp", ExitConversation);
            Response("wagngl_fthagn", ExitConversation);
            Response("pleeese", ExitConversation);
            Response("screw_password", ExitConversation);
        }
    }

    // KNOW_SPATHI_PASSWORD from Fwiffo or a visit, not only the Melnorme's
    // word before any visit (SPATHI_HOME_VISITS 7 with Fwiffo not found).
    private bool PasswordTold() =>
        GetGameState("KNOW_SPATHI_PASSWORD") != 0
        && (GetGameState("FOUND_PLUTO_SPATHI") != 0 || GetGameState("SPATHI_HOME_VISITS") != 7);

    protected internal override void Intro()
    {
        int manner = GetGameState("SPATHI_MANNER");
        if (manner == 2)
        {
            NPCPhrase("HATE_YOU_FOREVER");
            T.SetSegue(Segue.Hostile);
        }
        else if (manner == 1 && PasswordTold())
            SpathiAngry(0);
        else if (T.CheckAlliance(SpathiShip) == Story.Allied.Good)
        {
            T.SetColorMap(1);
            SpathiAllies(0);
        }
        else if (GetGameState("SPATHI_PARTY") != 0)
        {
            T.SetColorMap(1);
            SpathiParty(0);
        }
        else if (GetGameState("SPATHI_QUEST") != 0)
        {
            if (GetGameState("LIED_ABOUT_CREATURES") < 2)
            {
                T.SetColorMap(1);
                SpathiQuest(0);
            }
            else
            {
                NPCPhrase("YOU_LIED_2");

                SetGameState("SPATHI_MANNER", 2);
                T.SetSegue(Segue.Hostile);
            }
        }
        else if (GetGameState("KNOW_SPATHI_QUEST") != 0)
        {
            T.SetColorMap(1);
            LearnQuest(0);
        }
        else if (PasswordTold())
        {
            T.SetColorMap(1);
            SpathiCouncil(0);
        }
        else
            SpathiPassword(0);
    }

    protected internal override void PostEncounter()
    {
        int manner;
        if (T.Segue == Segue.Hostile && (manner = GetGameState("SPATHI_MANNER")) != 2)
        {
            SetGameState("SPATHI_MANNER", 1);
            if (manner != 1)
            {
                SetGameState("SPATHI_VISITS", 0);
                SetGameState("SPATHI_HOME_VISITS", 0);
            }
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        // The alternate "Safe Ones" track, if there is one.
        T.AltSong = "comm.safeones.music";
        T.UseAltSong = true;

        if (GetGameState("SPATHI_MANNER") == 3)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
