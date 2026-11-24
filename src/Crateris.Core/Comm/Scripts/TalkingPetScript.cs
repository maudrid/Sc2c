// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/talkpet/talkpet.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Talking Pet (talkpet.c), whole. Nothing in play starts it before
/// stage 3c (its world, genpet.c's Pet half, and the Pet as a device are
/// 3c's: the stage 3b-2 plan's item 10): the tests run it. At peace from
/// init but before the last battle. At the Umgah's world with its zombies
/// about it (the enemy's ships there, GetHeadLink (&amp;GLOBAL
/// (npc_built_ship_q)): <see cref="Talk.EnemyShips"/>): the first time its
/// questions (the ship's name spliced into "we are"; the zombies asked
/// about once they're known, KNOW_UMGAH_ZOMBIES), every way out of them
/// its compulsion: the captain hypnotised (PLAYER_HYPNOTIZED, which 3c's
/// world answers with the jump to the Ur-Quan), or, with the Taalo
/// protector aboard, "I can't compel you" and a fight; every later time
/// (TALKING_PET_HOME_VISITS) the compulsion at once. With the zombies
/// beaten, or the Umgah gone (StartSphereTracking (UMGAH_SHIP) 0: "the
/// Umgah are all gone"), its deal: the questions, its lie about its kind
/// (DNYARRI_LIED; LEARNED_TALKING_PET says whether the players know
/// better), and "kill you" down to its plea, which flashes the
/// mind-control strobe (<see cref="MindControlStrobe"/>); every way out
/// of the deal brings it aboard (TALKING_PET, TALKING_PET_ON_SHIP, the
/// Umgah freed: UMGAH_ZOMBIE_BLOBBIES 0, their visits counted afresh).
/// Aboard, as a device used (3c): its greetings by visit (the first, about
/// the Taalo protector, skipped without one), its news and suggestions
/// (TALKING_PET_INFO, TALKING_PET_SUGGESTIONS), a ship to compel
/// (SHIP_TO_COMPEL), its race asked about down to its powers, which flash
/// the strobe twice, and its lie thrown at it. Before the Sa-Matra
/// (READY_TO_CONFUSE_URQUAN): what's still missing (CHMMR_BOMB_STATE,
/// UTWIG_BOMB_ON_SHIP), or the Ur-Quan compelled (URQUAN_MESSED_UP). The
/// last battle's pep talk (by GLOBAL (glob_flags)'s CYBORG_ENABLED:
/// <see cref="Talk.CyborgEnabled"/>), hostile, and the endgame's
/// OUT_TAKES. Every path is checked against UQM's own script
/// (tests/golden/talks-talkpet.json). talkpet.c keeps nothing of its own
/// between replies, and gives nothing before its goodbye, so a talk
/// dropped unfinished leaves nothing to settle.
/// </summary>
public sealed class TalkingPetScript : CommScript
{
    // races.h's UMGAH_SHIP.
    private const int UmgahShip = 17;

    /// <summary>The mind-control strobe's place among the ambient animations (talkpet.c's AlienAmbientArray[16]).</summary>
    public const int StrobeAnimation = 16;

    public TalkingPetScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Peace);
        SetGameState("SHIP_TO_COMPEL", 0);

        if (PlayerSaid(r, "compel_urquan"))
        {
            NPCPhrase("HERE_WE_GO");

            SetGameState("URQUAN_MESSED_UP", 1);
        }
        else if (PlayerSaid(r, "wish_to_go_now"))
            NPCPhrase("EXCELLENT_IDEA");
        else if (PlayerSaid(r, "bye_onboard"))
            NPCPhrase("GOODBYE_ONBOARD");
        else if (PlayerSaid(r, "compel_that_ship"))
            NPCPhrase("SAVING_MY_POWER");
        else if (PlayerSaid(r, "ok_lets_do_it")
                 || PlayerSaid(r, "want_kill_1")
                 || PlayerSaid(r, "want_kill_2")
                 || PlayerSaid(r, "want_kill_3"))
        {
            if (PlayerSaid(r, "ok_lets_do_it"))
                NPCPhrase("COMING_ABOARD");
            else
                NPCPhrase("GLAD_YOU_WONT_KILL");

            SetGameState("TALKING_PET", 1);
            SetGameState("TALKING_PET_ON_SHIP", 1);
            SetGameState("UMGAH_ZOMBIE_BLOBBIES", 0);
            SetGameState("UMGAH_VISITS", 0);
            SetGameState("UMGAH_HOME_VISITS", 0);
            SetGameState("ARILOU_STACK_2", 0);
        }
    }

    private void MindFuckUrquan(int r)
    {
        if (PlayerSaid(r, "what_now"))
        {
            NPCPhrase("DO_THIS");

            DisablePhrase("what_now");
        }
        else if (PlayerSaid(r, "im_scared"))
        {
            NPCPhrase("STUPID_FOP");

            DisablePhrase("im_scared");
        }

        if (PhraseEnabled("what_now"))
            Response("what_now", MindFuckUrquan);
        if (PhraseEnabled("im_scared"))
            Response("im_scared", MindFuckUrquan);
        Response("compel_urquan", ExitConversation);
    }

    /// <summary>
    /// MindControlStrobe: the one-shot strobe animation turned on
    /// (CommData.AlienAmbientArray[16].AnimFlags &amp;= ~ANIM_DISABLED). UQM's
    /// animation turns itself off again as it ends, so each call plays it
    /// once; the talk's flags stay on from the first (as the recordings
    /// have them), and each call is a start of its own for the screens
    /// (<see cref="Talk.StartAmbient"/>).
    /// </summary>
    private void MindControlStrobe() => T.StartAmbient(StrobeAnimation);

    private void MindControl(int r)
    {
        Action<int> respFunc;
        string reply;

        if (PlayerSaid(r, "what_about_powers"))
        {
            NPCPhrase("NOT_POWERS_BUT_FLOWERS");

            respFunc = MindControl;
            reply = "yes_flowers";
        }
        else /* if (R == yes_flowers) */
        {
            NPCPhrase("GOOD_HUMAN");

            respFunc = ExitConversation;
            reply = "wish_to_go_now";
        }

        T.AlienTalkSegue(Talk.WaitAll);
        MindControlStrobe();

        Response(reply, respFunc);
    }

    private void PetInfo(int r)
    {
        if (PlayerSaid(r, "about_your_race"))
            NPCPhrase("WHAT_ABOUT_RACE");
        else if (PlayerSaid(r, "what_about_physiology"))
        {
            NPCPhrase("NO_TALK_ABOUT_SELF");

            DisablePhrase("what_about_physiology");
        }
        else if (PlayerSaid(r, "what_about_your_history"))
        {
            NPCPhrase("ABOUT_HISTORY");

            DisablePhrase("what_about_your_history");
        }
        else if (PlayerSaid(r, "sentient_milieu"))
        {
            NPCPhrase("ABOUT_SENTIENT_MILIEU");

            DisablePhrase("sentient_milieu");
        }
        else if (PlayerSaid(r, "what_about_war"))
        {
            NPCPhrase("ABOUT_WAR");

            DisablePhrase("what_about_war");
        }

        if (PhraseEnabled("what_about_physiology"))
            Response("what_about_physiology", PetInfo);
        else
            Response("what_about_powers", MindControl);
        if (PhraseEnabled("what_about_your_history"))
            Response("what_about_your_history", PetInfo);
        else if (PhraseEnabled("sentient_milieu"))
            Response("sentient_milieu", PetInfo);
        else if (PhraseEnabled("what_about_war"))
            Response("what_about_war", PetInfo);
        Response("enough_info", PetDevice);
    }

    // A counted line: the eighth is said again from then on.
    private void Counted(string field, string prefix)
    {
        int numVisits = GetGameState(field);
        if (numVisits is >= 0 and <= 7)
            NPCPhrase($"{prefix}{numVisits + 1}");
        if (numVisits == 7)
            --numVisits;
        ++numVisits;
        SetGameState(field, numVisits);
    }

    private void PetDevice(int r)
    {
        if (PlayerSaid(r, "whats_up_onboard"))
        {
            Counted("TALKING_PET_INFO", "GENERAL_INFO_ONBOARD_");

            DisablePhrase("whats_up_onboard");
        }
        else if (PlayerSaid(r, "any_suggestions"))
        {
            Counted("TALKING_PET_SUGGESTIONS", "SUGGESTION_");

            DisablePhrase("any_suggestions");
        }
        else if (PlayerSaid(r, "enough_info"))
            NPCPhrase("OK_ENOUGH_INFO");
        else if (PlayerSaid(r, "you_lied"))
        {
            NPCPhrase("SO_WHAT");

            SetGameState("DNYARRI_LIED", 0);
        }

        if (GetGameState("SHIP_TO_COMPEL") != 0)
            Response("compel_that_ship", ExitConversation);
        if (PhraseEnabled("whats_up_onboard"))
            Response("whats_up_onboard", PetDevice);
        if (PhraseEnabled("any_suggestions"))
            Response("any_suggestions", PetDevice);
        Response("about_your_race", PetInfo);
        if (GetGameState("DNYARRI_LIED") != 0 && GetGameState("LEARNED_TALKING_PET") != 0)
            Response("you_lied", PetDevice);
        Response("bye_onboard", ExitConversation);
    }

    private void CompelPlayer(int r)
    {
        int lastStack = 0;
        var pStr = new string?[3];
        if (PlayerSaid(r, "what_are_you"))
        {
            NPCPhrase("JUST_TALKING_PET");

            DisablePhrase("what_are_you");
        }
        else if (PlayerSaid(r, "what_do_to_umgah"))
        {
            NPCPhrase("DID_NOTHING");

            DisablePhrase("what_do_to_umgah");
            lastStack = 1;
        }
        else if (PlayerSaid(r, "we_are_vindicator0"))
        {
            NPCPhrase("GOOD_FOR_YOU");

            DisablePhrase("we_are_vindicator0");
            lastStack = 2;
        }
        else if (r != 0)
        {
            if (PlayerSaid(r, "bye_at_umgah"))
                NPCPhrase("GOODBYE_AT_UMGAH");
            else if (PlayerSaid(r, "must_explain_presence"))
                NPCPhrase("EXPLAIN_NOTHING_MONKEY_BOY");
            else if (PlayerSaid(r, "umgah_zombies"))
                NPCPhrase("WORKS_LIKE_THIS");
            else if (PlayerSaid(r, "talking_pets_dumb"))
                NPCPhrase("OH_NO_YOU_DONT");

            SetGameState("KNOW_UMGAH_ZOMBIES", 1);
            if (GetGameState("TAALO_PROTECTOR_ON_SHIP") == 0)
                SetGameState("PLAYER_HYPNOTIZED", 1);
            else
            {
                NPCPhrase("CANT_COMPEL");

                T.SetSegue(Segue.Hostile);
            }

            return;
        }

        if (PhraseEnabled("what_are_you"))
            pStr[0] = "what_are_you";
        else
            pStr[0] = "talking_pets_dumb";
        if (GetGameState("KNOW_UMGAH_ZOMBIES") != 0)
        {
            if (PhraseEnabled("what_do_to_umgah"))
                pStr[1] = "what_do_to_umgah";
            else
                pStr[1] = "umgah_zombies";
        }
        if (PhraseEnabled("we_are_vindicator0"))
        {
            T.ConstructResponse("we_are_vindicator0", Sis.ShipName, "we_are_vindicator1");
            pStr[2] = "we_are_vindicator0";
        }
        else
            pStr[2] = "must_explain_presence";

        if (pStr[lastStack] is { } last)
            Offer(last);
        for (int i = 0; i < 3; ++i)
        {
            if (i != lastStack && pStr[i] is { } other)
                Offer(other);
        }
        Response("bye_at_umgah", CompelPlayer);

        void Offer(string id)
        {
            if (id != "we_are_vindicator0")
                Response(id, CompelPlayer);
            else
                T.DoResponsePhrase(id, CompelPlayer, Talk.SharedPhraseBuffer);
        }
    }

    private void KillPet(int r)
    {
        if (PlayerSaid(r, "must_kill"))
        {
            NPCPhrase("DONT_KILL");
            T.AlienTalkSegue(Talk.WaitAll);

            MindControlStrobe();
        }

        Response("want_kill_1", ExitConversation);
        Response("want_kill_2", ExitConversation);
        Response("want_kill_3", ExitConversation);
    }

    private void PetDeal(int r)
    {
        if (PlayerSaid(r, "what_kind_of_deal"))
        {
            NPCPhrase("HELP_DEFEAT_URQUAN");

            DisablePhrase("what_kind_of_deal");
        }
        else if (PlayerSaid(r, "how_trust"))
        {
            NPCPhrase("TRUST");

            DisablePhrase("how_trust");
        }
        else if (PlayerSaid(r, "boneless_dweeb"))
        {
            NPCPhrase("YOUR_BONELESS_DWEEB");

            DisablePhrase("boneless_dweeb");
        }
        else if (PlayerSaid(r, "what_are_you_really"))
        {
            NPCPhrase("POOR_DNYARRI");

            DisablePhrase("what_are_you_really");
        }
        else if (PlayerSaid(r, "hard_to_believe"))
        {
            NPCPhrase("ITS_TRUE");

            SetGameState("DNYARRI_LIED", 1);
            DisablePhrase("hard_to_believe");
        }
        else if (PlayerSaid(r, "bullshit"))
        {
            NPCPhrase("WORTH_A_TRY");

            DisablePhrase("bullshit");
        }
        else if (PlayerSaid(r, "kill_you"))
        {
            NPCPhrase("PLEASE_DONT");

            DisablePhrase("kill_you");
        }

        if (PhraseEnabled("what_kind_of_deal"))
            Response("what_kind_of_deal", PetDeal);
        else
        {
            if (PhraseEnabled("how_trust"))
                Response("how_trust", PetDeal);
            else if (PhraseEnabled("boneless_dweeb"))
                Response("boneless_dweeb", PetDeal);
            Response("ok_lets_do_it", ExitConversation);
        }
        if (PhraseEnabled("what_are_you_really"))
            Response("what_are_you_really", PetDeal);
        else
        {
            if (PhraseEnabled("hard_to_believe") && GetGameState("LEARNED_TALKING_PET") == 0)
                Response("hard_to_believe", PetDeal);
            else if (PhraseEnabled("bullshit") && GetGameState("LEARNED_TALKING_PET") != 0)
                Response("bullshit", PetDeal);
        }
        if (PhraseEnabled("kill_you"))
            Response("kill_you", PetDeal);
        else if (PhraseEnabled("must_kill"))
            Response("must_kill", KillPet);
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

        if (T.Activity == TalkActivity.InLastBattle)
        {
            SetGameState("SHIP_TO_COMPEL", 0);
            T.SetSegue(Segue.Hostile);
            if (!T.CyborgEnabled)
                NPCPhrase("HUMAN_PEP_TALK");
            else
                NPCPhrase("CYBORG_PEP_TALK");
        }
        else if (GetGameState("READY_TO_CONFUSE_URQUAN") != 0)
        {
            SetGameState("SHIP_TO_COMPEL", 0);
            SetGameState("READY_TO_CONFUSE_URQUAN", 0);
            SetGameState("AWARE_OF_SAMATRA", 1);
            if (GetGameState("CHMMR_BOMB_STATE") != 3)
            {
                NPCPhrase("HAVENT_GOT_EVERYTHING");
                if (GetGameState("UTWIG_BOMB_ON_SHIP") == 0)
                    NPCPhrase("NEED_BOMB");
                else
                    NPCPhrase("SOUP_UP_BOMB");

                T.SetSegue(Segue.Peace);
            }
            else if (GetGameState("URQUAN_MESSED_UP") != 0)
            {
                NPCPhrase("HELLO_AFTER_COMPEL_URQUAN");

                T.SetSegue(Segue.Peace);
            }
            else
            {
                NPCPhrase("I_SENSE_MY_SLAVES");

                MindFuckUrquan(0);
            }
        }
        else if (GetGameState("TALKING_PET_ON_SHIP") != 0)
        {
            numVisits = GetGameState("TALKING_PET_VISITS");

            // The Pet can be had without the Taalo protector once the
            // Kohr-Ah have wiped out the Umgah: HELLO_AS_DEVICE_1's
            // complaints about the protector make no sense then.
            if (GetGameState("TAALO_PROTECTOR_ON_SHIP") == 0 && numVisits == 0)
                ++numVisits; // skip HELLO_AS_DEVICE_1

            if (numVisits is >= 0 and <= 7)
                NPCPhrase($"HELLO_AS_DEVICE_{numVisits + 1}");
            if (numVisits == 7)
                --numVisits;
            ++numVisits;
            SetGameState("TALKING_PET_VISITS", numVisits);

            PetDevice(0);
        }
        else if (T.EnemyShips)   // GetHeadLink (&GLOBAL (npc_built_ship_q))
        {
            numVisits = GetGameState("TALKING_PET_HOME_VISITS");
            switch (numVisits++)
            {
                case 0:
                    SetGameState("UMGAH_VISITS", 0);
                    NPCPhrase("HELLO_AT_UMGAH");
                    break;
                case 1:
                    NPCPhrase("HYPNOTIZE_AGAIN_1");
                    break;
                case 2:
                    NPCPhrase("HYPNOTIZE_AGAIN_2");
                    break;
                case 3:
                    NPCPhrase("HYPNOTIZE_AGAIN_3");
                    break;
                case 4:
                    NPCPhrase("HYPNOTIZE_AGAIN_4");
                    --numVisits;
                    break;
            }
            SetGameState("TALKING_PET_HOME_VISITS", numVisits);

            if (numVisits == 1)
                CompelPlayer(0);
            else if (GetGameState("TAALO_PROTECTOR_ON_SHIP") == 0)
            {
                SetGameState("PLAYER_HYPNOTIZED", 1);
                T.SetSegue(Segue.Peace);
            }
            else
            {
                NPCPhrase("CANT_COMPEL");

                T.SetSegue(Segue.Hostile);
            }
        }
        else
        {
            if (T.StartSphereTracking(UmgahShip) != 0)
                NPCPhrase("LETS_MAKE_A_DEAL");
            else
            {
                NPCPhrase("UMGAH_ALL_GONE");

                // The how_trust and TRUST exchange only makes sense when the
                // players met the Pet before, so it tried to kill them, and
                // they have the Taalo protector.
                if (GetGameState("TALKING_PET_HOME_VISITS") == 0
                    || GetGameState("TAALO_PROTECTOR_ON_SHIP") == 0)
                    DisablePhrase("how_trust");
            }

            PetDeal(0);
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if (T.Activity != TalkActivity.InLastBattle)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
