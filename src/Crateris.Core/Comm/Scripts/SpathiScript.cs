// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/spathi/spathic.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Spathi's talk away from Spathiwa (spathic.c), whole: the Spathi
/// met in space by SPATHI_MANNER (neutral, angry after a fight, hating
/// for good, friendly once on their quest, allied), and Fwiffo on Pluto
/// (FOUND_PLUTO_SPATHI, which stage 3c's Pluto sets), who may join the
/// escorts alone in his Eluder. Orbiting Spathiwa's moon runs another
/// script, the Spathi at home (spahome.c, init_race by
/// GLOBAL_FLAGS_AND_DATA bit 7), which is 3b-2's. Every path is checked
/// against UQM's own script (tests/golden/talks-spathi.json).
/// </summary>
public sealed class SpathiScript : CommScript
{
    // races.h's SPATHI_SHIP; build.h's NAME_OFFSET + NUM_CAPTAINS_NAMES (Fwiffo).
    private const int SpathiShip = 6, Fwiffo = 5 + 16;

    // spathic.c's static join_us_refusals: set to 0 as Fwiffo's talk starts,
    // so it lasts the talk.
    private int _joinUsRefusals;

    public SpathiScript(ConversationScript phrases) : base(phrases)
    {
    }

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Peace);

        if (PlayerSaid(r, "bye_ally_space"))
            NPCPhrase("GOODBYE_ALLY_SPACE");
        else if (PlayerSaid(r, "bye_friendly_space"))
            NPCPhrase("GOODBYE_FRIENDLY_SPACE");
        else if (PlayerSaid(r, "part_in_peace"))
            NPCPhrase("KEEP_IT_SECRET");
        else if (PlayerSaid(r, "we_sorry_space"))
            NPCPhrase("APOLOGIZE_AT_HOMEWORLD");
        else if (PlayerSaid(r, "give_info_space"))
            NPCPhrase("HERES_SOME_INFO");
        else if (PlayerSaid(r, "bye_angry_space"))
            NPCPhrase("GOODBYE_ANGRY_SPACE");
        else if (PlayerSaid(r, "attack_you_now"))
        {
            NPCPhrase("YIPES");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "we_fight_again_space"))
        {
            NPCPhrase("OK_FIGHT_AGAIN_SPACE");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "die_slugboy") || PlayerSaid(r, "we_fight_1") || PlayerSaid(r, "we_fight_2")
                 || PlayerSaid(r, "pay_for_crimes") || PlayerSaid(r, "tell_me_coordinates")
                 || PlayerSaid(r, "changed_mind"))
        {
            if (PlayerSaid(r, "tell_me_coordinates"))
                NPCPhrase("FAKE_COORDINATES");
            NPCPhrase("OK_WE_FIGHT_AT_PLUTO");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "join_us"))
        {
            if (Sis.EscortRoom == 0)   // EscortFeasibilityStudy (SPATHI_SHIP)
                NPCPhrase("TOO_SCARY");
            else
            {
                NPCPhrase("WILL_JOIN");

                T.AlienTalkSegue(Talk.WaitAll);
                T.AddEscortShips(SpathiShip, 1);
                // Make the Eluder escort captained by Fwiffo alone.
                T.SetEscortCrewComplement(SpathiShip, 1, Fwiffo);
            }
        }
    }

    private void SpathiOnPluto(int r)
    {
        if (PlayerSaid(r, "hi_there"))
            NPCPhrase("ARE_YOU_SURE");
        else if (PlayerSaid(r, "dont_kill"))
            NPCPhrase("OK_WONT");
        else if (PlayerSaid(r, "youre_forgiven"))
            NPCPhrase("THANKS_FOR_FORGIVENESS");
        else if (PlayerSaid(r, "you_wont_die_yet"))
            NPCPhrase("ETERNAL_GRATITUDE");
        else if (PlayerSaid(r, "you_may_live"))
            NPCPhrase("GEE_THANKS");
        else if (PlayerSaid(r, "youve_got_me_all_wrong"))
            NPCPhrase("SORRY_NO_COORDS");
        else if (PlayerSaid(r, "what_doing_on_pluto_1"))
        {
            NPCPhrase("ABOUT_20_YEARS_AGO");

            DisablePhrase("what_doing_on_pluto_1");
        }
        else if (PlayerSaid(r, "what_doing_on_pluto_2"))
        {
            NPCPhrase("WHEN_URQUAN_ARRIVED");

            DisablePhrase("what_doing_on_pluto_2");
        }
        else if (PlayerSaid(r, "what_doing_on_pluto_3"))
        {
            NPCPhrase("STATIONED_ON_EARTH_MOON");

            DisablePhrase("what_doing_on_pluto_3");
        }
        else if (PlayerSaid(r, "what_about_ilwrath"))
        {
            NPCPhrase("ABOUT_ILWRATH");

            DisablePhrase("what_about_ilwrath");
        }
        else if (PlayerSaid(r, "when_ilwrath"))
        {
            NPCPhrase("THEN_ILWRATH");

            DisablePhrase("when_ilwrath");
        }
        else if (PlayerSaid(r, "what_about_moonbase"))
        {
            NPCPhrase("SET_UP_BASE");

            DisablePhrase("what_about_moonbase");
        }
        else if (PlayerSaid(r, "what_about_other_spathi"))
        {
            NPCPhrase("SPATHI_ARE");

            DisablePhrase("what_about_other_spathi");
        }
        // (spathic.c tests what_about_other_spathi a second time here, for
        // THEN_ILWRATH: never reached.)
        else if (PlayerSaid(r, "how_many_crew"))
        {
            NPCPhrase("THOUSANDS");

            DisablePhrase("how_many_crew");
        }
        else if (PlayerSaid(r, "really_thousands"))
        {
            NPCPhrase("JUST_ME");

            DisablePhrase("really_thousands");
        }
        else if (PlayerSaid(r, "full_of_monsters"))
        {
            NPCPhrase("HOW_TRUE");

            DisablePhrase("full_of_monsters");
        }
        else if (PlayerSaid(r, "what_enemy"))
        {
            NPCPhrase("ENEMY_IS");

            DisablePhrase("what_enemy");
        }
        else if (PlayerSaid(r, "why_you_here"))
        {
            NPCPhrase("DREW_SHORT_STRAW");

            DisablePhrase("why_you_here");
        }
        else if (PlayerSaid(r, "where_are_urquan"))
        {
            NPCPhrase("URQUAN_LEFT");

            DisablePhrase("where_are_urquan");
        }
        else if (PlayerSaid(r, "what_about_other_races"))
        {
            NPCPhrase("ABOUT_OTHER_RACES");

            DisablePhrase("what_about_other_races");
        }
        else if (PlayerSaid(r, "what_blaze_of_glory"))
        {
            NPCPhrase("BLAZE_IS");

            DisablePhrase("what_blaze_of_glory");
        }
        else if (PlayerSaid(r, "what_about_yourself"))
        {
            NPCPhrase("ABOUT_MYSELF");

            DisablePhrase("what_about_yourself");
        }
        else if (PlayerSaid(r, "join_us"))
        {
            if (_joinUsRefusals == 0)
            {
                NPCPhrase("WONT_JOIN_1");
                ++_joinUsRefusals;
            }
            else if (_joinUsRefusals == 1)
            {
                NPCPhrase("WONT_JOIN_2");
                ++_joinUsRefusals;
            }
            else
                NPCPhrase("WONT_JOIN_3");
        }

        if (PhraseEnabled("what_doing_on_pluto_1"))
            Response("what_doing_on_pluto_1", SpathiOnPluto);
        else if (PhraseEnabled("what_doing_on_pluto_2"))
            Response("what_doing_on_pluto_2", SpathiOnPluto);
        else if (PhraseEnabled("what_doing_on_pluto_3"))
            Response("what_doing_on_pluto_3", SpathiOnPluto);
        else
        {
            if (PhraseEnabled("what_about_ilwrath"))
                Response("what_about_ilwrath", SpathiOnPluto);
            else if (PhraseEnabled("when_ilwrath"))
                Response("when_ilwrath", SpathiOnPluto);

            if (PhraseEnabled("what_about_moonbase"))
                Response("what_about_moonbase", SpathiOnPluto);
            else if (PhraseEnabled("what_about_other_spathi"))
                Response("what_about_other_spathi", SpathiOnPluto);
            else
            {
                if (PhraseEnabled("how_many_crew"))
                    Response("how_many_crew", SpathiOnPluto);
                else if (PhraseEnabled("really_thousands"))
                    Response("really_thousands", SpathiOnPluto);
                else if (PhraseEnabled("full_of_monsters"))
                    Response("full_of_monsters", SpathiOnPluto);

                if (PhraseEnabled("what_enemy"))
                    Response("what_enemy", SpathiOnPluto);
                else if (PhraseEnabled("why_you_here"))
                    Response("why_you_here", SpathiOnPluto);
            }
        }
        if (PhraseEnabled("where_are_urquan"))
            Response("where_are_urquan", SpathiOnPluto);
        else if (PhraseEnabled("what_about_other_races"))
            Response("what_about_other_races", SpathiOnPluto);
        else if (PhraseEnabled("what_blaze_of_glory"))
            Response("what_blaze_of_glory", SpathiOnPluto);
        else if (PhraseEnabled("what_about_yourself"))
            Response("what_about_yourself", SpathiOnPluto);

        if (!PhraseEnabled("full_of_monsters"))
            Response("join_us", ExitConversation);
        else
            Response("join_us", SpathiOnPluto);
        Response("changed_mind", ExitConversation);
    }

    private void SpathiMustGrovel(int r)
    {
        if (PlayerSaid(r, "identify"))
        {
            NPCPhrase("I_FWIFFO");

            Response("do_cultural", SpathiMustGrovel);
            Response("youre_forgiven", SpathiOnPluto);
            Response("die_slugboy", ExitConversation);
        }
        else if (PlayerSaid(r, "do_cultural"))
        {
            NPCPhrase("WEZZY_WEZZAH");

            Response("begin_ritual", SpathiMustGrovel);
            Response("you_wont_die_yet", SpathiOnPluto);
            Response("we_fight_2", ExitConversation);
        }
        else if (PlayerSaid(r, "begin_ritual"))
        {
            NPCPhrase("MUST_DO_RITUAL_AT_HOME");

            Response("you_may_live", SpathiOnPluto);
            Response("pay_for_crimes", ExitConversation);
            Response("what_are_coordinates", SpathiMustGrovel);
        }
        else /* if (R == what_are_coordinates) */
        {
            NPCPhrase("COORDINATES_ARE");

            Response("youve_got_me_all_wrong", SpathiOnPluto);
            Response("tell_me_coordinates", ExitConversation);
        }
    }

    private void SpathiAllies(int r)
    {
        if (r == 0)
        {
            int numVisits = GetGameState("SPATHI_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("INIT_ALLIED_HELLO_SPACE");
                    break;
                case 1:
                    NPCPhrase("SUBSEQUENT_ALLIED_HELLO_SPACE");
                    --numVisits;
                    break;
            }
            SetGameState("SPATHI_VISITS", numVisits);
        }
        else if (PlayerSaid(r, "whats_up_space_2"))
        {
            NPCPhrase("GENERAL_INFO_SPACE_2");

            DisablePhrase("whats_up_space_2");
        }
        else if (PlayerSaid(r, "give_us_info_from_space"))
        {
            NPCPhrase("GET_INFO_FROM_SPATHIWA");

            DisablePhrase("give_us_info_from_space");
        }
        else if (PlayerSaid(r, "give_us_resources_space"))
        {
            NPCPhrase("GET_RESOURCES_FROM_SPATHIWA");

            DisablePhrase("give_us_resources_space");
        }
        else if (PlayerSaid(r, "what_do_for_fun"))
        {
            NPCPhrase("DO_THIS_FOR_FUN");

            DisablePhrase("what_do_for_fun");
        }

        if (PhraseEnabled("whats_up_space_2"))
            Response("whats_up_space_2", SpathiAllies);
        if (PhraseEnabled("give_us_info_from_space"))
            Response("give_us_info_from_space", SpathiAllies);
        if (PhraseEnabled("give_us_resources_space"))
            Response("give_us_resources_space", SpathiAllies);
        if (PhraseEnabled("what_do_for_fun"))
            Response("what_do_for_fun", SpathiAllies);
        Response("bye_ally_space", ExitConversation);
    }

    private void SpathiFriendly(int r)
    {
        if (r == 0)
        {
            int numVisits = GetGameState("SPATHI_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("INIT_FRIENDLY_HELLO_SPACE");
                    break;
                case 1:
                    NPCPhrase("SUBSEQUENT_FRIENDLY_HELLO_SPACE");
                    --numVisits;
                    break;
            }
            SetGameState("SPATHI_VISITS", numVisits);
        }
        else if (PlayerSaid(r, "since_friendly_give_stuff"))
        {
            NPCPhrase("GIVE_ADVICE");

            DisablePhrase("since_friendly_give_stuff");
        }
        else if (PlayerSaid(r, "whats_up_space_1"))
        {
            NPCPhrase("GENERAL_INFO_SPACE_1");

            DisablePhrase("whats_up_space_1");
        }

        if (PhraseEnabled("whats_up_space_1"))
            Response("whats_up_space_1", SpathiFriendly);
        if (PhraseEnabled("since_friendly_give_stuff"))
            Response("since_friendly_give_stuff", SpathiFriendly);
        Response("bye_friendly_space", ExitConversation);
    }

    private void SpathiBefriend(int r)
    {
        bool infoLeft = false;
        int lastStack = 0;
        var pStr = new string?[2];
        if (PlayerSaid(r, "come_in_peace"))
            NPCPhrase("AGAINST_NATURE");
        else if (PlayerSaid(r, "looking_for_a_few_good_squids"))
        {
            NPCPhrase("URQUAN_SLAVES");

            DisablePhrase("looking_for_a_few_good_squids");
        }
        else if (PlayerSaid(r, "why_slaves"))
        {
            NPCPhrase("UMGAH_TRICK");

            DisablePhrase("why_slaves");
        }
        else if (PlayerSaid(r, "tell_us_about_you"))
        {
            NPCPhrase("ABOUT_US");

            DisablePhrase("tell_us_about_you");
            lastStack = 1;
        }
        else if (PlayerSaid(r, "what_you_really_want"))
        {
            NPCPhrase("WANT_THIS");

            DisablePhrase("what_you_really_want");
        }
        else if (PlayerSaid(r, "how_about_alliance"))
        {
            NPCPhrase("SURE");

            DisablePhrase("how_about_alliance");
        }

        if (PhraseEnabled("looking_for_a_few_good_squids"))
            pStr[0] = "looking_for_a_few_good_squids";
        else if (PhraseEnabled("why_slaves"))
            pStr[0] = "why_slaves";
        if (PhraseEnabled("tell_us_about_you"))
            pStr[1] = "tell_us_about_you";
        else if (PhraseEnabled("what_you_really_want"))
            pStr[1] = "what_you_really_want";
        if (pStr[lastStack] is { } first)
        {
            infoLeft = true;
            Response(first, SpathiBefriend);
        }
        lastStack ^= 1;
        if (pStr[lastStack] is { } second)
        {
            infoLeft = true;
            Response(second, SpathiBefriend);
        }
        if (PhraseEnabled("how_about_alliance"))
        {
            infoLeft = true;
            Response("how_about_alliance", SpathiBefriend);
        }

        if (!infoLeft)
        {
            SetGameState("SPATHI_STACK1", 1);
            SpathiNeutral(r);
        }
    }

    private void SpathiAntagonize(int r)
    {
        if (PlayerSaid(r, "prepare_to_die"))
        {
            NPCPhrase("ALWAYS_PREPARED");

            SetGameState("SPATHI_STACK2", 1);
        }
        else if (PlayerSaid(r, "heard_youre_cowards"))
        {
            NPCPhrase("DARN_TOOTIN");

            DisablePhrase("heard_youre_cowards");
        }
        else if (PlayerSaid(r, "wanna_fight"))
        {
            NPCPhrase("YES_WE_DO");

            DisablePhrase("wanna_fight");
        }
        else if (PlayerSaid(r, "so_lets_fight"))
        {
            NPCPhrase("OK_LETS_FIGHT");

            DisablePhrase("so_lets_fight");
        }
        else if (PlayerSaid(r, "so_lets_fight_already"))
        {
            NPCPhrase("DONT_REALLY_WANT_TO_FIGHT");

            DisablePhrase("so_lets_fight_already");
        }

        if (PhraseEnabled("wanna_fight"))
            Response("wanna_fight", SpathiAntagonize);
        else if (PhraseEnabled("so_lets_fight"))
            Response("so_lets_fight", SpathiAntagonize);
        else if (PhraseEnabled("so_lets_fight_already"))
            Response("so_lets_fight_already", SpathiAntagonize);
        if (PhraseEnabled("heard_youre_cowards"))
            Response("heard_youre_cowards", SpathiAntagonize);
        Response("attack_you_now", ExitConversation);
    }

    private void SpathiNeutral(int r)
    {
        if (r == 0)
        {
            int numVisits = GetGameState("SPATHI_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("INIT_NEUTRAL_HELLO_SPACE");
                    break;
                case 1:
                    NPCPhrase("SUBSEQUENT_NEUTRAL_HELLO_SPACE");
                    --numVisits;
                    break;
            }
            SetGameState("SPATHI_VISITS", numVisits);
        }
        else if (PlayerSaid(r, "look_weird"))
        {
            NPCPhrase("YOU_LOOK_WEIRD");

            SetGameState("SPATHI_STACK0", 1);
        }
        else if (PlayerSaid(r, "no_look_really_weird"))
        {
            NPCPhrase("NO_YOU_LOOK_REALLY_WEIRD");

            SetGameState("SPATHI_STACK0", 2);
        }

        switch (GetGameState("SPATHI_STACK0"))
        {
            case 0:
                Response("look_weird", SpathiNeutral);
                break;
            case 1:
                Response("no_look_really_weird", SpathiNeutral);
                break;
        }
        if (GetGameState("SPATHI_STACK1") == 0)
            Response("come_in_peace", SpathiBefriend);
        if (GetGameState("SPATHI_STACK2") == 0)
            Response("prepare_to_die", SpathiAntagonize);
        else
            Response("attack_you_now", ExitConversation);
        Response("part_in_peace", ExitConversation);
    }

    protected internal override void Intro()
    {
        int manner = GetGameState("SPATHI_MANNER");
        if (GetGameState("FOUND_PLUTO_SPATHI") == 1)
        {
            _joinUsRefusals = 0;

            NPCPhrase("SORRY_ABOUT_THAT");

            // If already know password from Melnorme, but haven't visited Spathiwa yet . . .
            if (GetGameState("SPATHI_HOME_VISITS") == 7)
            {
                SetGameState("KNOW_SPATHI_PASSWORD", 0);
                SetGameState("SPATHI_HOME_VISITS", 0);
            }

            Response("identify", SpathiMustGrovel);
            Response("hi_there", SpathiOnPluto);
            Response("dont_kill", SpathiOnPluto);
            Response("we_fight_1", ExitConversation);
        }
        else if (manner == 2)
        {
            NPCPhrase("HATE_YOU_FOREVER_SPACE");
            T.SetSegue(Segue.Hostile);
        }
        else if (manner == 1)
        {
            int numVisits = GetGameState("SPATHI_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("INIT_ANGRY_HELLO_SPACE");
                    break;
                case 1:
                    NPCPhrase("SUBSEQUENT_ANGRY_HELLO_SPACE");
                    --numVisits;
                    break;
            }
            SetGameState("SPATHI_VISITS", numVisits);

            Response("give_info_space", ExitConversation);
            Response("we_sorry_space", ExitConversation);
            Response("we_fight_again_space", ExitConversation);
            Response("bye_angry_space", ExitConversation);
        }
        else if (T.CheckAlliance(SpathiShip) == Story.Allied.Good)
            SpathiAllies(0);
        else if (GetGameState("SPATHI_QUEST") != 0)
            SpathiFriendly(0);
        else
            SpathiNeutral(0);
    }

    protected internal override void PostEncounter()
    {
        int manner;

        if (GetGameState("FOUND_PLUTO_SPATHI") == 1)
            SetGameState("FOUND_PLUTO_SPATHI", 2);
        else if (T.Segue == Segue.Hostile && (manner = GetGameState("SPATHI_MANNER")) != 2)
        {
            SetGameState("SPATHI_MANNER", 1);
            if (manner != 1)
            {
                SetGameState("SPATHI_VISITS", 0);
                // If don't know about Spathi via Melnorme . . .
                if (GetGameState("SPATHI_HOME_VISITS") != 7)
                    SetGameState("SPATHI_HOME_VISITS", 0);
            }
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if (GetGameState("FOUND_PLUTO_SPATHI") == 1)
        {
            // Use the alternate Fwiffo track if available.
            T.AltSong = "comm.fwiffo.music";
            T.UseAltSong = true;
        }
        else
            T.UseAltSong = false;   // regular track -- let's make sure

        if (GetGameState("FOUND_PLUTO_SPATHI") == 1 || GetGameState("SPATHI_MANNER") == 3
            || T.Activity == TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
