// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/syreen/syreenc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Syreen (syreenc.c), whole: at their starbase (gensyr.c), never met
/// in space (RACE_COMMUNICATION has no Syreen conversation), and at peace
/// from init: no encounter screen and no fight. Before they know of the
/// Mycon (SYREEN_KNOW_ABOUT_MYCON): their greetings by visit
/// (SYREEN_HOME_VISITS), the first two with the captain's introductions
/// (the captain's and the ship's names spliced into "we are", and into
/// the welcome's text where there's no voice), then their questions and
/// stacks (SYREEN_STACK0 to 2), the shattered world and the Mycon's part
/// (KNOW_ABOUT_SHATTERED, KNOW_SYREEN_WORLD_SHATTERED, SYREEN_WANT_PROOF),
/// and with an egg case aboard (stage 3c's pickups) the horrible truth:
/// SYREEN_KNOW_ABOUT_MYCON and the shuttle given (SYREEN_SHUTTLE,
/// SYREEN_SHUTTLE_ON_SHIP: stage 3c's device). Then the vault (stage 3c's
/// world): looking for it (its hint), found (SHIP_VAULT_UNLOCKED: the
/// ambush planned, MYCON_AMBUSH, and the reward's foreplay), the ambush
/// ready, and after it (MYCON_KNOW_AMBUSH) their alliance at the first
/// greeting (SetRaceAllied), their news (SYREEN_INFO) and the reward
/// (PLAYER_HAVING_SEX, PLAYER_HAD_SEX; the colour maps faded as the
/// lights go down and up again). Every path is checked against UQM's own
/// script (tests/golden/talks-syreen.json).
/// </summary>
public sealed class SyreenScript : CommScript
{
    // races.h's SYREEN_SHIP.
    private const int SyreenShip = 20;

    public SyreenScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private void FriendlyExit(int r)
    {
        T.SetSegue(Segue.Peace);

        if (PlayerSaid(r, "bye"))
            NPCPhrase("GOODBYE");
        else if (PlayerSaid(r, "im_on_my_way") || PlayerSaid(r, "doing_this_for_you") || PlayerSaid(r, "if_i_die"))
            NPCPhrase("GOOD_LUCK");
        else if (PlayerSaid(r, "bye_before_vault"))
            NPCPhrase("GOODBYE_BEFORE_VAULT");
        else if (PlayerSaid(r, "bye_after_vault"))
            NPCPhrase("GOODBYE_AFTER_VAULT");
        else if (PlayerSaid(r, "bye_before_ambush"))
            NPCPhrase("GOODBYE_BEFORE_AMBUSH");
        else if (PlayerSaid(r, "bye_after_ambush"))
            NPCPhrase("GOODBYE_AFTER_AMBUSH");
        else
        {
            if (PlayerSaid(r, "hands_off"))
                NPCPhrase("OK_WONT_USE_HANDS");
            else if (PlayerSaid(r, "not_much_more_to_say"))
                NPCPhrase("THEN_STOP_TALKING");
            NPCPhrase("LATER");
            NPCPhrase("SEX_GOODBYE");

            T.AlienTalkSegue(2);
            T.XFormColorMap(0, Talk.OneSecond / 2);
            T.AlienTalkSegue(Talk.WaitAll);

            SetGameState("PLAYER_HAD_SEX", 1);
            SetGameState("PLAYER_HAVING_SEX", 0);
        }
    }

    private void Sex(int r)
    {
        if (PlayerSaid(r, "in_the_spirit"))
            NPCPhrase("OK_SPIRIT");
        else if (PlayerSaid(r, "what_in_mind"))
            NPCPhrase("SOMETHING_LIKE_THIS");
        else if (PlayerSaid(r, "disease"))
            NPCPhrase("JUST_RELAX");
        else if (PlayerSaid(r, "what_happens_if_i_touch_this"))
        {
            NPCPhrase("THIS_HAPPENS");

            DisablePhrase("what_happens_if_i_touch_this");
        }
        else if (PlayerSaid(r, "are_you_sure_this_is_ok"))
        {
            NPCPhrase("YES_SURE");

            DisablePhrase("are_you_sure_this_is_ok");
        }
        else if (PlayerSaid(r, "boy_they_never_taught"))
        {
            NPCPhrase("THEN_LET_ME_TEACH");

            DisablePhrase("boy_they_never_taught");
        }

        if (!PhraseEnabled("what_happens_if_i_touch_this") && !PhraseEnabled("are_you_sure_this_is_ok")
            && !PhraseEnabled("boy_they_never_taught"))
            Response("not_much_more_to_say", FriendlyExit);
        else
        {
            if (PhraseEnabled("what_happens_if_i_touch_this"))
                Response("what_happens_if_i_touch_this", Sex);
            if (PhraseEnabled("are_you_sure_this_is_ok"))
                Response("are_you_sure_this_is_ok", Sex);
            if (PhraseEnabled("boy_they_never_taught"))
                Response("boy_they_never_taught", Sex);
        }
    }

    private void Foreplay(int r)
    {
        if (PlayerSaid(r, "whats_my_reward") || PlayerSaid(r, "what_about_us"))
        {
            if (PlayerSaid(r, "whats_my_reward"))
                NPCPhrase("HERES_REWARD");
            else
                NPCPhrase("ABOUT_US");
            NPCPhrase("MORE_COMFORTABLE");
            T.AlienTalkSegue(1);
            T.XFormColorMap(1, Talk.OneSecond);
            T.AlienTalkSegue(Talk.WaitAll);

            SetGameState("PLAYER_HAVING_SEX", 1);
        }
        else if (PlayerSaid(r, "why_lights_off"))
        {
            NPCPhrase("LIGHTS_OFF_BECAUSE");

            DisablePhrase("why_lights_off");
        }
        else if (PlayerSaid(r, "evil_monster"))
        {
            NPCPhrase("NOT_EVIL_MONSTER");

            DisablePhrase("evil_monster");
        }

        if (PhraseEnabled("why_lights_off"))
            Response("why_lights_off", Foreplay);
        else if (PhraseEnabled("evil_monster"))
            Response("evil_monster", Foreplay);
        else
            Response("disease", Sex);
        Response("in_the_spirit", Sex);
        Response("what_in_mind", Sex);
        Response("hands_off", FriendlyExit);
    }

    private void AfterAmbush(int r)
    {
        if (PlayerSaid(r, "what_now_after_ambush"))
        {
            NPCPhrase("DO_THIS_AFTER_AMBUSH");

            DisablePhrase("what_now_after_ambush");
        }
        else if (PlayerSaid(r, "what_about_you"))
        {
            NPCPhrase("ABOUT_ME");

            DisablePhrase("what_about_you");
        }
        else if (PlayerSaid(r, "whats_up_after_ambush"))
        {
            int numVisits = Greeting(GetGameState("SYREEN_INFO"), "GENERAL_INFO_AFTER_AMBUSH_1",
                "GENERAL_INFO_AFTER_AMBUSH_2", "GENERAL_INFO_AFTER_AMBUSH_3", "GENERAL_INFO_AFTER_AMBUSH_4");
            SetGameState("SYREEN_INFO", numVisits);

            DisablePhrase("whats_up_after_ambush");
        }

        if (PhraseEnabled("what_about_you"))
            Response("what_about_you", AfterAmbush);
        else if (GetGameState("PLAYER_HAD_SEX") == 0)
            Response("what_about_us", Foreplay);
        if (PhraseEnabled("what_now_after_ambush"))
            Response("what_now_after_ambush", AfterAmbush);
        if (PhraseEnabled("whats_up_after_ambush"))
            Response("whats_up_after_ambush", AfterAmbush);
        Response("bye_after_ambush", FriendlyExit);
    }

    private void AmbushReady(int r)
    {
        if (PlayerSaid(r, "repeat_plan"))
        {
            NPCPhrase("OK_REPEAT_PLAN");

            DisablePhrase("repeat_plan");
        }

        if (PhraseEnabled("repeat_plan"))
            Response("repeat_plan", AmbushReady);
        Response("bye_before_ambush", FriendlyExit);
    }

    private void SyreenShuttle(int r)
    {
        if (PlayerSaid(r, "whats_next_step"))
        {
            NPCPhrase("OPEN_VAULT");

            DisablePhrase("whats_next_step");
        }
        else if (PlayerSaid(r, "what_do_i_get_for_this"))
        {
            NPCPhrase("GRATITUDE");

            DisablePhrase("what_do_i_get_for_this");
        }
        else if (PlayerSaid(r, "not_sure"))
        {
            NPCPhrase("PLEASE");

            DisablePhrase("not_sure");
        }
        else if (PlayerSaid(r, "where_is_it"))
        {
            NPCPhrase("DONT_KNOW_WHERE");
            NPCPhrase("GIVE_SHUTTLE");

            SetGameState("SYREEN_SHUTTLE", 1);
            SetGameState("SYREEN_SHUTTLE_ON_SHIP", 1);

            DisablePhrase("where_is_it");
        }
        else if (PlayerSaid(r, "been_there"))
        {
            NPCPhrase("GREAT");
            NPCPhrase("GIVE_SHUTTLE");

            SetGameState("SYREEN_SHUTTLE", 1);
            SetGameState("SYREEN_SHUTTLE_ON_SHIP", 1);

            DisablePhrase("been_there");
        }

        if (PhraseEnabled("whats_next_step"))
            Response("whats_next_step", SyreenShuttle);
        else
        {
            if (GetGameState("KNOW_SYREEN_VAULT") == 0)
            {
                if (PhraseEnabled("where_is_it"))
                    Response("where_is_it", SyreenShuttle);
            }
            else
            {
                if (PhraseEnabled("been_there"))
                    Response("been_there", SyreenShuttle);
            }
            if (!PhraseEnabled("where_is_it") || !PhraseEnabled("been_there"))
            {
                Response("im_on_my_way", FriendlyExit);
                Response("doing_this_for_you", FriendlyExit);
                Response("if_i_die", FriendlyExit);
            }
        }
        if (PhraseEnabled("what_do_i_get_for_this"))
            Response("what_do_i_get_for_this", SyreenShuttle);
        if (PhraseEnabled("not_sure"))
            Response("not_sure", SyreenShuttle);
    }

    private void NormalSyreen(int r)
    {
        int lastStack = 0;
        var pStr = new string?[4];
        if (PlayerSaid(r, "we_here_to_help"))
            NPCPhrase("NO_NEED_HELP");
        else if (PlayerSaid(r, "we_need_help"))
            NPCPhrase("CANT_GIVE_HELP");
        else if (PlayerSaid(r, "know_about_deep_children"))
        {
            NPCPhrase("WHAT_ABOUT_DEEP_CHILDREN");

            DisablePhrase("know_about_deep_children");
        }
        else if (PlayerSaid(r, "mycons_involved"))
        {
            NPCPhrase("WHAT_PROOF");

            SetGameState("KNOW_ABOUT_SHATTERED", 3);
        }
        else if (PlayerSaid(r, "have_no_proof"))
        {
            NPCPhrase("NEED_PROOF");

            SetGameState("SYREEN_WANT_PROOF", 1);
        }
        else if (PlayerSaid(r, "have_proof"))
        {
            NPCPhrase("SEE_PROOF");

            DisablePhrase("have_proof");
        }
        else if (PlayerSaid(r, "what_doing_here"))
        {
            NPCPhrase("OUR_NEW_WORLD");

            SetGameState("SYREEN_STACK0", 1);
            lastStack = 1;
        }
        else if (PlayerSaid(r, "what_about_war"))
        {
            NPCPhrase("ABOUT_WAR");

            SetGameState("SYREEN_STACK0", 2);
            lastStack = 1;
        }
        else if (PlayerSaid(r, "help_us"))
        {
            NPCPhrase("WONT_HELP");

            SetGameState("SYREEN_STACK0", 3);
        }
        else if (PlayerSaid(r, "what_about_history"))
        {
            NPCPhrase("BEFORE_WAR");

            SetGameState("SYREEN_STACK1", 1);
            lastStack = 2;
        }
        else if (PlayerSaid(r, "what_about_homeworld"))
        {
            NPCPhrase("ABOUT_HOMEWORLD");

            SetGameState("SYREEN_STACK1", 2);
            lastStack = 2;
        }
        else if (PlayerSaid(r, "what_happened"))
        {
            NPCPhrase("DONT_KNOW_HOW");

            SetGameState("KNOW_SYREEN_WORLD_SHATTERED", 1);
            SetGameState("SYREEN_STACK1", 3);
        }
        else if (PlayerSaid(r, "what_about_outfit"))
        {
            NPCPhrase("HOPE_YOU_LIKE_IT");

            SetGameState("SYREEN_STACK2", 1);
            lastStack = 3;
        }
        else if (PlayerSaid(r, "where_mates"))
        {
            NPCPhrase("MATES_KILLED");

            SetGameState("SYREEN_STACK2", 2);
            lastStack = 3;
        }
        else if (PlayerSaid(r, "get_lonely"))
        {
            NPCPhrase("MAKE_OUT_ALL_RIGHT");

            SetGameState("SYREEN_STACK2", 3);
        }
        else if (PlayerSaid(r, "look_at_egg_sacks"))
        {
            NPCPhrase("HORRIBLE_TRUTH");

            T.SetSegue(Segue.Peace);
            SetGameState("SYREEN_HOME_VISITS", 0);
            SetGameState("SYREEN_KNOW_ABOUT_MYCON", 1);

            SyreenShuttle(0);
            return;
        }

        if (GetGameState("KNOW_ABOUT_SHATTERED") < 3)
        {
            if (GetGameState("KNOW_ABOUT_SHATTERED") == 2 && GetGameState("KNOW_SYREEN_WORLD_SHATTERED") != 0)
            {
                if (PhraseEnabled("know_about_deep_children"))
                    pStr[0] = "know_about_deep_children";
                else
                    pStr[0] = "mycons_involved";
            }
        }
        else
        {
            if (GetGameState("EGG_CASE0_ON_SHIP") != 0 || GetGameState("EGG_CASE1_ON_SHIP") != 0
                || GetGameState("EGG_CASE2_ON_SHIP") != 0)
            {
                if (PhraseEnabled("have_proof"))
                    pStr[0] = "have_proof";
                else
                    pStr[0] = "look_at_egg_sacks";
            }
            else if (GetGameState("SYREEN_WANT_PROOF") == 0)
                pStr[0] = "have_no_proof";
        }
        switch (GetGameState("SYREEN_STACK0"))
        {
            case 0:
                pStr[1] = "what_doing_here";
                break;
            case 1:
                pStr[1] = "what_about_war";
                break;
            case 2:
                pStr[1] = "help_us";
                break;
        }
        switch (GetGameState("SYREEN_STACK1"))
        {
            case 0:
                pStr[2] = "what_about_history";
                break;
            case 1:
                pStr[2] = "what_about_homeworld";
                break;
            case 2:
                pStr[2] = "what_happened";
                break;
        }
        switch (GetGameState("SYREEN_STACK2"))
        {
            case 0:
                pStr[3] = "what_about_outfit";
                break;
            case 1:
                pStr[3] = "where_mates";
                break;
            case 2:
                pStr[3] = "get_lonely";
                break;
        }
        if (pStr[lastStack] is { } last)
            Response(last, NormalSyreen);
        for (int i = 0; i < 4; ++i)
        {
            if (i != lastStack && pStr[i] is { } other)
                Response(other, NormalSyreen);
        }
        Response("bye", FriendlyExit);
    }

    private void InitialSyreen(int r)
    {
        if (PlayerSaid(r, "we_are_vice_squad"))
        {
            NPCPhrase("OK_VICE");
            NPCPhrase("HOW_CAN_YOU_BE_HERE");
        }
        else if (PlayerSaid(r, "we_are_the_one_for_you_baby"))
        {
            NPCPhrase("MAYBE_CAPTAIN");
            NPCPhrase("HOW_CAN_YOU_BE_HERE");
        }
        else if (PlayerSaid(r, "we_are_vindicator0"))
        {
            NPCPhrase("WELCOME_VINDICATOR0");
            if (!T.UsingSpeech)
            {
                NPCPhrase(Talk.GlobalPlayerName);
                NPCPhrase("WELCOME_VINDICATOR1");
                NPCPhrase(Talk.GlobalShipName);
                NPCPhrase("WELCOME_VINDICATOR2");
            }
            NPCPhrase("HOW_CAN_YOU_BE_HERE");
        }
        else if (PlayerSaid(r, "we_are_impressed"))
        {
            NPCPhrase("SO_AM_I_CAPTAIN");
            NPCPhrase("HOW_CAN_YOU_BE_HERE");
        }
        else if (PlayerSaid(r, "i_need_you"))
        {
            NPCPhrase("OK_NEED");

            DisablePhrase("i_need_you");
            DisablePhrase("i_need_touch_o_vision");
        }
        else if (PlayerSaid(r, "i_need_touch_o_vision"))
        {
            NPCPhrase("TOUCH_O_VISION");

            DisablePhrase("i_need_you");
            DisablePhrase("i_need_touch_o_vision");
        }

        Response("we_here_to_help", NormalSyreen);
        Response("we_need_help", NormalSyreen);
        if (PhraseEnabled("i_need_you"))
            Response("i_need_you", InitialSyreen);
        if (PhraseEnabled("i_need_touch_o_vision"))
            Response("i_need_touch_o_vision", InitialSyreen);
    }

    private void PlanAmbush(int r)
    {
        NPCPhrase("OK_FOUND_VAULT");

        SetGameState("MYCON_AMBUSH", 1);
        // This is redundant but left here for clarity (syreenc.c's).
        SetGameState("SYREEN_HOME_VISITS", 0);

        Response("whats_my_reward", Foreplay);
        Response("bye_after_vault", FriendlyExit);
    }

    private void SyreenVault(int r)
    {
        if (PlayerSaid(r, "vault_hint"))
        {
            NPCPhrase("OK_HINT");

            DisablePhrase("vault_hint");
        }

        if (PhraseEnabled("vault_hint"))
            Response("vault_hint", SyreenVault);
        Response("bye_before_vault", FriendlyExit);
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
        if (T.Activity == TalkActivity.WonLastBattle)
        {
            NPCPhrase("OUT_TAKES");

            T.SetSegue(Segue.Peace);
            return;
        }

        int numVisits = GetGameState("SYREEN_HOME_VISITS");
        if (GetGameState("MYCON_KNOW_AMBUSH") != 0)
        {
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("HELLO_AFTER_AMBUSH_1");
                    T.SetRaceAllied(SyreenShip, true);
                    break;
                case 1:
                    NPCPhrase("HELLO_AFTER_AMBUSH_2");
                    break;
                case 2:
                    NPCPhrase("HELLO_AFTER_AMBUSH_3");
                    break;
                case 3:
                    NPCPhrase("HELLO_AFTER_AMBUSH_3");
                    --numVisits;
                    break;
            }

            AfterAmbush(0);
        }
        else if (GetGameState("MYCON_AMBUSH") != 0)
        {
            numVisits = Greeting(numVisits, "READY_FOR_AMBUSH");

            AmbushReady(0);
        }
        else if (GetGameState("SYREEN_KNOW_ABOUT_MYCON") == 0)
        {
            numVisits = Greeting(numVisits, "HELLO_BEFORE_AMBUSH_1", "HELLO_BEFORE_AMBUSH_2",
                "HELLO_BEFORE_AMBUSH_3", "HELLO_BEFORE_AMBUSH_4");

            if (numVisits > 1)
                NormalSyreen(0);
            else
            {
                T.ConstructResponse("we_are_vindicator0", Sis.CaptainName, "we_are_vindicator1",
                    Sis.ShipName, "we_are_vindicator2");
                Response("we_are_vice_squad", InitialSyreen);
                Response("we_are_the_one_for_you_baby", InitialSyreen);
                T.DoResponsePhrase("we_are_vindicator0", InitialSyreen, Talk.SharedPhraseBuffer);
                Response("we_are_impressed", InitialSyreen);
            }
        }
        // (The shuttle's MUST_ACT, before SYREEN_SHUTTLE, is #ifdef NEVER in syreenc.c.)
        else if (GetGameState("SHIP_VAULT_UNLOCKED") != 0)
        {
            PlanAmbush(0);
            // syreenc.c's: PlanAmbush's SYREEN_HOME_VISITS 0 would be undone
            // below, so the greetings' count is set 0 here as well.
            numVisits = 0;
        }
        else
        {
            numVisits = Greeting(numVisits, "FOUND_VAULT_YET_1", "FOUND_VAULT_YET_2");

            SyreenVault(0);
        }
        SetGameState("SYREEN_HOME_VISITS", (byte)numVisits);
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        T.SetSegue(Segue.Peace);
    }
}
