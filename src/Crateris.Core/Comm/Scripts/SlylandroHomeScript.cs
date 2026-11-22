// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/slyhome/slyhome.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Slylandro at home (slyhome.c), whole: the gas giant's talk
/// (gensly.c, SLYLANDRO_HOME_CONVERSATION, no ships, always at peace).
/// Its stacks of questions about the visitors, the Slylandro, the humans
/// and the probes; once the probe's bug is shown (PROBE_EXHIBITED_BUG)
/// and the player knows how its programming works, the self-destruct code
/// (DESTRUCT_CODE_ON_SHIP, which the probes' talk sends: an instant
/// victory) and the ramp-down it starts (SLYLANDRO_MULTIPLIER up by one,
/// SLYLANDRO_RAMP_DOWN run at once, which takes it down by one every 23
/// days to 0); the recall greeting after. Every path is checked against
/// UQM's own script (tests/golden/talks-slyhome.json).
/// </summary>
public sealed class SlylandroHomeScript : CommScript
{
    public SlylandroHomeScript(ConversationScript phrases) : base(phrases)
    {
    }

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Peace);

        switch (GetGameState("SLYLANDRO_HOME_VISITS"))
        {
            case 1:
                NPCPhrase("GOODBYE_1");
                break;
            default:
                NPCPhrase("GOODBYE_2");
                break;
        }
    }

    private void HumanInfo(int r)
    {
        if (PlayerSaid(r, "happy_to_tell_more"))
        {
            NPCPhrase("TELL_MORE");

            SetGameState("SLYLANDRO_STACK4", 1);
        }
        else if (PlayerSaid(r, "would_you_like_to_know_more"))
            NPCPhrase("YES_TELL_MORE");
        else if (PlayerSaid(r, "we_come_from_earth"))
        {
            NPCPhrase("OK_EARTH");

            SetGameState("SLYLANDRO_KNOW_EARTH", 1);
        }
        else if (PlayerSaid(r, "we_explore"))
        {
            NPCPhrase("OK_EXPLORE");

            SetGameState("SLYLANDRO_KNOW_EXPLORE", 1);
        }
        else if (PlayerSaid(r, "we_fight_urquan"))
        {
            NPCPhrase("URQUAN_NICE_GUYS");

            SetGameState("SLYLANDRO_KNOW_URQUAN", 1);
        }
        else if (PlayerSaid(r, "not_same_urquan"))
        {
            NPCPhrase("PERSONALITY_CHANGE");

            SetGameState("SLYLANDRO_KNOW_URQUAN", 2);
        }
        else if (PlayerSaid(r, "we_gather"))
        {
            NPCPhrase("MAYBE_INTERESTED");

            SetGameState("SLYLANDRO_KNOW_GATHER", 1);
        }

        bool infoLeft = false;
        if (GetGameState("SLYLANDRO_KNOW_URQUAN") == 1)
        {
            infoLeft = true;
            Response("not_same_urquan", HumanInfo);
        }
        if (GetGameState("SLYLANDRO_KNOW_EARTH") == 0)
        {
            infoLeft = true;
            Response("we_come_from_earth", HumanInfo);
        }
        if (GetGameState("SLYLANDRO_KNOW_EXPLORE") == 0)
        {
            infoLeft = true;
            Response("we_explore", HumanInfo);
        }
        if (GetGameState("SLYLANDRO_KNOW_URQUAN") == 0)
        {
            infoLeft = true;
            Response("we_fight_urquan", HumanInfo);
        }
        if (GetGameState("SLYLANDRO_KNOW_GATHER") == 0)
        {
            infoLeft = true;
            Response("we_gather", HumanInfo);
        }

        Response("enough_about_me", HomeWorld);
        if (!infoLeft)
            SetGameState("SLYLANDRO_STACK4", 2);
    }

    private void SlylandroInfo(int r)
    {
        if (PlayerSaid(r, "like_more_about_you"))
            NPCPhrase("SURE_KNOW_WHAT");
        else if (PlayerSaid(r, "what_about_home"))
        {
            NPCPhrase("ABOUT_HOME");

            DisablePhrase("what_about_home");
        }
        else if (PlayerSaid(r, "what_about_culture"))
        {
            NPCPhrase("ABOUT_CULTURE");

            DisablePhrase("what_about_culture");
        }
        else if (PlayerSaid(r, "what_about_history"))
        {
            NPCPhrase("ABOUT_HISTORY");

            DisablePhrase("what_about_history");
        }
        else if (PlayerSaid(r, "what_about_biology"))
        {
            NPCPhrase("ABOUT_BIOLOGY");

            DisablePhrase("what_about_biology");
        }

        bool infoLeft = false;
        if (PhraseEnabled("what_about_home"))
        {
            infoLeft = true;
            Response("what_about_home", SlylandroInfo);
        }
        if (PhraseEnabled("what_about_culture"))
        {
            infoLeft = true;
            Response("what_about_culture", SlylandroInfo);
        }
        if (PhraseEnabled("what_about_history"))
        {
            infoLeft = true;
            Response("what_about_history", SlylandroInfo);
        }
        if (PhraseEnabled("what_about_biology"))
        {
            infoLeft = true;
            Response("what_about_biology", SlylandroInfo);
        }

        Response("enough_info", HomeWorld);
        if (!infoLeft)
            DisablePhrase("like_more_about_you");
    }

    private void FixBug(int r)
    {
        if (PlayerSaid(r, "think_about_rep_priorities"))
            NPCPhrase("UH_OH");
        else if (PlayerSaid(r, "hunt_them_down"))
        {
            NPCPhrase("GROW_TOO_FAST");

            DisablePhrase("hunt_them_down");
        }
        else if (PlayerSaid(r, "sue_melnorme"))
        {
            NPCPhrase("SIGNED_WAIVER");

            DisablePhrase("sue_melnorme");
        }
        else if (PlayerSaid(r, "recall_signal"))
        {
            NPCPhrase("NOT_THIS_MODEL");

            DisablePhrase("recall_signal");
        }

        if (PhraseEnabled("hunt_them_down"))
            Response("hunt_them_down", FixBug);
        if (PhraseEnabled("sue_melnorme"))
            Response("sue_melnorme", FixBug);
        if (PhraseEnabled("recall_signal"))
            Response("recall_signal", FixBug);
        Response("mega_self_destruct", HomeWorld);
    }

    private void ProbeBug(int r)
    {
        if (PlayerSaid(r, "probe_has_bug"))
            NPCPhrase("NO_IT_DOESNT");
        else if (PlayerSaid(r, "tell_me_about_rep_2"))
        {
            NPCPhrase("REP_NO_PROBLEM");

            DisablePhrase("tell_me_about_rep_2");
        }
        else if (PlayerSaid(r, "what_about_rep_priorities"))
        {
            NPCPhrase("MAXIMUM_SO_WHAT");

            DisablePhrase("what_about_rep_priorities");
        }
        else if (PlayerSaid(r, "tell_me_about_attack"))
        {
            NPCPhrase("ATTACK_NO_PROBLEM");

            DisablePhrase("tell_me_about_attack");
        }

        if (PhraseEnabled("tell_me_about_rep_2"))
            Response("tell_me_about_rep_2", ProbeBug);
        else if (PhraseEnabled("what_about_rep_priorities"))
            Response("what_about_rep_priorities", ProbeBug);
        else
            Response("think_about_rep_priorities", FixBug);
        if (PhraseEnabled("tell_me_about_attack"))
            Response("tell_me_about_attack", ProbeBug);
    }

    private void ProbeFunction(int r)
    {
        int lastStack = 0;
        var pStr = new string?[2];
        if (PlayerSaid(r, "talk_more_probe_attack"))
            NPCPhrase("NO_PROBLEM_BUT_SURE");
        else if (PlayerSaid(r, "tell_me_about_basics"))
        {
            NPCPhrase("BASIC_COMMANDS");

            SetGameState("PLAYER_KNOWS_PROGRAM", 1);
            DisablePhrase("tell_basics_again");
        }
        else if (PlayerSaid(r, "tell_basics_again"))
        {
            NPCPhrase("OK_BASICS_AGAIN");

            DisablePhrase("tell_basics_again");
        }
        else if (PlayerSaid(r, "what_effect"))
        {
            NPCPhrase("AFFECTS_BEHAVIOR");

            SetGameState("PLAYER_KNOWS_EFFECTS", 1);
            DisablePhrase("what_effect");
        }
        else if (PlayerSaid(r, "tell_me_about_rep_1"))
        {
            NPCPhrase("ABOUT_REP");

            lastStack = 2;
            SetGameState("SLYLANDRO_STACK8", 3);
        }
        else if (PlayerSaid(r, "what_set_priority"))
        {
            NPCPhrase("MAXIMUM");

            SetGameState("PLAYER_KNOWS_PRIORITY", 1);
            DisablePhrase("what_set_priority");
        }
        else if (PlayerSaid(r, "how_does_probe_defend"))
        {
            NPCPhrase("ONLY_SELF_DEFENSE");

            lastStack = 1;
            SetGameState("SLYLANDRO_STACK9", 1);
        }
        else if (PlayerSaid(r, "combat_behavior"))
        {
            NPCPhrase("MISSILE_BATTERIES");

            lastStack = 1;
            SetGameState("SLYLANDRO_STACK9", 2);
        }
        else if (PlayerSaid(r, "what_missile_batteries"))
        {
            NPCPhrase("LIGHTNING_ONLY_FOR_HARVESTING");

            SetGameState("SLYLANDRO_STACK9", 3);
        }

        switch (GetGameState("SLYLANDRO_STACK9"))
        {
            case 0:
                pStr[0] = "how_does_probe_defend";
                break;
            case 1:
                pStr[0] = "combat_behavior";
                break;
            case 2:
                pStr[0] = "what_missile_batteries";
                break;
        }
        switch (GetGameState("SLYLANDRO_STACK8"))
        {
            case 2:
                pStr[1] = "tell_me_about_rep_1";
                break;
            case 3:
                if (PhraseEnabled("what_set_priority"))
                    pStr[1] = "what_set_priority";
                break;
        }

        if (lastStack != 0 && pStr[lastStack - 1] is { } last)
            Response(last, ProbeFunction);
        if (GetGameState("PLAYER_KNOWS_PROGRAM") == 0)
            Response("tell_me_about_basics", ProbeFunction);
        else
        {
            if (GetGameState("PLAYER_KNOWS_PRIORITY") != 0)
            {
                if (GetGameState("PLAYER_KNOWS_EFFECTS") != 0)
                    Response("probe_has_bug", ProbeBug);
                if (PhraseEnabled("what_effect"))
                    Response("what_effect", ProbeFunction);
            }
            if (PhraseEnabled("tell_basics_again"))
                Response("tell_basics_again", ProbeFunction);
        }
        if (lastStack == 0)
        {
            do
            {
                if (pStr[lastStack] is { } s)
                    Response(s, ProbeFunction);
            } while (++lastStack < 2);
        }
        else
        {
            lastStack = (lastStack - 1) ^ 1;
            if (pStr[lastStack] is { } s)
                Response(s, ProbeFunction);
        }

        Response("enough_problem", ProbeInfo);
    }

    private void ProbeInfo(int r)
    {
        int lastStack = 0;
        var pStr = new string?[3];
        if (PlayerSaid(r, "what_are_probes"))
        {
            NPCPhrase("PROBES_ARE");

            SetGameState("SLYLANDRO_STACK5", 1);
        }
        else if (PlayerSaid(r, "know_more_probe"))
            NPCPhrase("OK_WHAT_MORE_PROBE");
        else if (PlayerSaid(r, "why_probe_always_attack"))
        {
            NPCPhrase("ONLY_DEFEND");

            SetGameState("SLYLANDRO_STACK6", 1);
        }
        else if (PlayerSaid(r, "talk_more_probe_attack"))
        {
            ProbeFunction(r);
            return;
        }
        else if (PlayerSaid(r, "where_probes_from"))
        {
            NPCPhrase("PROBES_FROM_MELNORME");

            lastStack = 1;
            SetGameState("SLYLANDRO_STACK7", 1);
        }
        else if (PlayerSaid(r, "why_sell"))
        {
            NPCPhrase("SELL_FOR_INFO");

            lastStack = 1;
            SetGameState("SLYLANDRO_STACK7", 2);
        }
        else if (PlayerSaid(r, "how_long_ago"))
        {
            NPCPhrase("FIFTY_THOUSAND_ROTATIONS");

            SetGameState("SLYLANDRO_STACK7", 3);
        }
        else if (PlayerSaid(r, "whats_probes_mission"))
        {
            NPCPhrase("SEEK_OUT_NEW_LIFE");

            lastStack = 2;
            SetGameState("SLYLANDRO_STACK8", 1);
        }
        else if (PlayerSaid(r, "if_only_one"))
        {
            NPCPhrase("THEY_REPLICATE");

            SetGameState("SLYLANDRO_STACK8", 2);
        }
        else if (PlayerSaid(r, "enough_problem"))
            NPCPhrase("OK_ENOUGH_PROBLEM");

        if (GetGameState("SLYLANDRO_KNOW_BROKEN") == 0 && GetGameState("PROBE_EXHIBITED_BUG") != 0)
        {
            switch (GetGameState("SLYLANDRO_STACK6"))
            {
                case 0:
                    pStr[0] = "why_probe_always_attack";
                    break;
                case 1:
                    pStr[0] = "talk_more_probe_attack";
                    break;
            }
        }
        switch (GetGameState("SLYLANDRO_STACK7"))
        {
            case 0:
                pStr[1] = "where_probes_from";
                break;
            case 1:
                pStr[1] = "why_sell";
                break;
            case 2:
                pStr[1] = "how_long_ago";
                break;
        }
        switch (GetGameState("SLYLANDRO_STACK8"))
        {
            case 0:
                pStr[2] = "whats_probes_mission";
                break;
            case 1:
                pStr[2] = "if_only_one";
                break;
        }

        bool infoLeft = false;
        if (pStr[lastStack] is { } first)
        {
            infoLeft = true;
            Response(first, ProbeInfo);
        }
        for (int i = 0; i < 3; ++i)
        {
            if (i != lastStack && pStr[i] is { } s)
            {
                infoLeft = true;
                Response(s, ProbeInfo);
            }
        }

        Response("enough_probe", HomeWorld);
        if (!infoLeft)
            DisablePhrase("know_more_probe");
    }

    // A HomeWorld reply: we_are_us0 with the names spliced in (construct_response's).
    private void HomeWorldResponse(string id)
    {
        if (id != "we_are_us0")
            Response(id, HomeWorld);
        else
            T.DoResponsePhrase(id, HomeWorld, Talk.SharedPhraseBuffer);
    }

    private void HomeWorld(int r)
    {
        int lastStack = 0;
        var pStr = new string?[3];
        if (PlayerSaid(r, "we_are_us0"))
        {
            NPCPhrase("TERRIBLY_EXCITING");

            SetGameState("SLYLANDRO_STACK1", 1);
            DisablePhrase("we_are_us0");
        }
        else if (PlayerSaid(r, "what_other_visitors"))
        {
            NPCPhrase("VISITORS");

            SetGameState("PLAYER_KNOWS_PROBE", 1);
            SetGameState("SLYLANDRO_STACK1", 2);
        }
        else if (PlayerSaid(r, "any_other_visitors"))
        {
            NPCPhrase("LONG_AGO");

            SetGameState("SLYLANDRO_STACK1", 3);
        }
        else if (PlayerSaid(r, "what_about_sentient_milieu"))
        {
            NPCPhrase("MET_TAALO_THEY_ARE_FROM");

            SetGameState("SLYLANDRO_STACK1", 4);
        }
        else if (PlayerSaid(r, "who_else"))
        {
            NPCPhrase("PRECURSORS");

            SetGameState("SLYLANDRO_STACK1", 5);
        }
        else if (PlayerSaid(r, "precursors_yow"))
        {
            NPCPhrase("ABOUT_PRECURSORS");

            SetGameState("SLYLANDRO_STACK1", 6);
        }
        else if (PlayerSaid(r, "must_know_more"))
        {
            NPCPhrase("ALL_WE_KNOW");

            SetGameState("SLYLANDRO_STACK1", 7);
        }
        else if (PlayerSaid(r, "who_are_you"))
        {
            NPCPhrase("WE_ARE_SLY");

            lastStack = 1;
            SetGameState("SLYLANDRO_STACK2", 1);
        }
        else if (PlayerSaid(r, "where_are_you"))
        {
            NPCPhrase("DOWN_HERE");

            lastStack = 2;
            SetGameState("SLYLANDRO_STACK3", 1);
        }
        else if (PlayerSaid(r, "thats_impossible_1"))
        {
            NPCPhrase("NO_ITS_NOT_1");

            lastStack = 2;
            SetGameState("SLYLANDRO_STACK3", 2);
        }
        else if (PlayerSaid(r, "thats_impossible_2"))
        {
            NPCPhrase("NO_ITS_NOT_2");

            lastStack = 2;
            SetGameState("SLYLANDRO_STACK3", 3);
        }
        else if (PlayerSaid(r, "like_more_about_you"))
        {
            SlylandroInfo(r);
            return;
        }
        else if (PlayerSaid(r, "enough_about_me"))
            NPCPhrase("OK_ENOUGH_YOU");
        else if (PlayerSaid(r, "enough_info"))
            NPCPhrase("OK_ENOUGH_INFO");
        else if (PlayerSaid(r, "enough_probe"))
            NPCPhrase("OK_ENOUGH_PROBE");
        else if (PlayerSaid(r, "mega_self_destruct"))
        {
            NPCPhrase("WHY_YES_THERE_IS");

            SetGameState("SLYLANDRO_KNOW_BROKEN", 1);
            SetGameState("DESTRUCT_CODE_ON_SHIP", 1);
            int i = (byte)(GetGameState("SLYLANDRO_MULTIPLIER") + 1);
            SetGameState("SLYLANDRO_MULTIPLIER", i);
            // Run at once (relative, all 0): the probes' ramp down.
            T.AddEvent(relative: true, 0, 0, 0, "SLYLANDRO_RAMP_DOWN");
        }

        switch (GetGameState("SLYLANDRO_STACK1"))
        {
            case 0:
                T.ConstructResponse("we_are_us0", Sis.CaptainName, "we_are_us1", Sis.ShipName, "we_are_us2");
                pStr[0] = "we_are_us0";
                break;
            case 1:
                pStr[0] = "what_other_visitors";
                break;
            case 2:
                pStr[0] = "any_other_visitors";
                break;
            case 3:
                pStr[0] = "what_about_sentient_milieu";
                break;
            case 4:
                pStr[0] = "who_else";
                break;
            case 5:
                pStr[0] = "precursors_yow";
                break;
            case 6:
                pStr[0] = "must_know_more";
                break;
        }
        switch (GetGameState("SLYLANDRO_STACK2"))
        {
            case 0:
                pStr[1] = "who_are_you";
                break;
            case 1:
                if (PhraseEnabled("like_more_about_you"))
                    pStr[1] = "like_more_about_you";
                break;
        }
        switch (GetGameState("SLYLANDRO_STACK3"))
        {
            case 0:
                pStr[2] = "where_are_you";
                break;
            case 1:
                pStr[2] = "thats_impossible_1";
                break;
            case 2:
                pStr[2] = "thats_impossible_2";
                break;
        }

        if (pStr[lastStack] is { } first)
            HomeWorldResponse(first);
        for (int k = 0; k < 3; ++k)
        {
            if (k != lastStack && pStr[k] is { } s)
                HomeWorldResponse(s);
        }
        if (GetGameState("SLYLANDRO_STACK1") != 0)
        {
            switch (GetGameState("SLYLANDRO_STACK4"))
            {
                case 0:
                    Response("happy_to_tell_more", HumanInfo);
                    break;
                case 1:
                    Response("would_you_like_to_know_more", HumanInfo);
                    break;
            }
        }
        if (GetGameState("PLAYER_KNOWS_PROBE") != 0 && GetGameState("SLYLANDRO_KNOW_BROKEN") == 0)
        {
            switch (GetGameState("SLYLANDRO_STACK5"))
            {
                case 0:
                    Response("what_are_probes", ProbeInfo);
                    break;
                case 1:
                    if (PhraseEnabled("know_more_probe"))
                        Response("know_more_probe", ProbeInfo);
                    break;
            }
        }
        Response("bye", ExitConversation);
    }

    protected internal override void Intro()
    {
        int numVisits;
        if (GetGameState("SLYLANDRO_KNOW_BROKEN") != 0 && (numVisits = GetGameState("RECALL_VISITS")) == 0)
        {
            NPCPhrase("RECALL_PROGRAM_1");
            ++numVisits;
            SetGameState("RECALL_VISITS", numVisits);
        }
        else
        {
            numVisits = GetGameState("SLYLANDRO_HOME_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("HELLO_1");
                    break;
                case 1:
                    NPCPhrase("HELLO_2");
                    break;
                case 2:
                    NPCPhrase("HELLO_3");
                    break;
                case 3:
                    NPCPhrase("HELLO_4");
                    --numVisits;
                    break;
            }
            SetGameState("SLYLANDRO_HOME_VISITS", numVisits);
        }

        HomeWorld(0);
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        T.SetSegue(Segue.Peace);
    }
}
