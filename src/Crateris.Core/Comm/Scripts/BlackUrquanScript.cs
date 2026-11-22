// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/blackur/blackurc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Kohr-Ah's talk (blackurc.c), whole: the Kohr-Ah met in space, before
/// and in their frenzy, the Sa-Matra's, once they've killed everyone, and
/// the endgame's won battle. Every path is checked against UQM's own
/// script (tests/golden/talks-blackur.json).
/// </summary>
public sealed class BlackUrquanScript : CommScript
{
    public BlackUrquanScript(ConversationScript phrases) : base(phrases)
    {
    }

    // blackurc.c's four-step counters, each a switch (NumVisits++) whose
    // cases say the next line, the last one again and again (--NumVisits),
    // then SET_GAME_STATE (field, NumVisits).
    private void Counted(string field, params string[] lines)
    {
        int numVisits = GetGameState(field);
        if (numVisits < lines.Length)
        {
            NPCPhrase(lines[numVisits]);
            if (numVisits == lines.Length - 1)
                --numVisits;
        }
        ++numVisits;
        SetGameState(field, numVisits);
    }

    private void CombatIsInevitable(int r)
    {
        T.SetSegue(Segue.Hostile);

        if (PlayerSaid(r, "bye"))
        {
            if (GetGameState("KOHR_AH_BYES") == 0)
                NPCPhrase("GOODBYE_AND_DIE");
            else
                NPCPhrase("DIE_HUMAN" /* GOODBYE_AND_DIE_2 */);

            SetGameState("KOHR_AH_BYES", 1);
        }
        else if (PlayerSaid(r, "guess_thats_all"))
            NPCPhrase("THEN_DIE");
        else if (PlayerSaid(r, "what_are_you_hovering_over"))
        {
            NPCPhrase("BONE_PILE");

            SetGameState("KOHR_AH_INFO", 1);
        }
        else if (PlayerSaid(r, "you_sure_are_creepy"))
        {
            NPCPhrase("YES_CREEPY");

            SetGameState("KOHR_AH_INFO", 2);
        }
        else if (PlayerSaid(r, "stop_that_gross_blinking"))
        {
            NPCPhrase("DIE_HUMAN");

            SetGameState("KOHR_AH_INFO", 3);
        }
        else if (PlayerSaid(r, "threat_1") || PlayerSaid(r, "threat_2")
            || PlayerSaid(r, "threat_3") || PlayerSaid(r, "threat_4"))
            Counted("KOHR_AH_REASONS", "RESISTANCE_IS_USELESS_1", "RESISTANCE_IS_USELESS_2",
                "RESISTANCE_IS_USELESS_3", "RESISTANCE_IS_USELESS_4");
        else if (PlayerSaid(r, "plead_1") || PlayerSaid(r, "plead_2")
            || PlayerSaid(r, "plead_3") || PlayerSaid(r, "plead_4"))
        {
            int numVisits = GetGameState("KOHR_AH_PLEAD");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("PLEADING_IS_USELESS_1");
                    break;
                case 1:
                    NPCPhrase("PLEADING_IS_USELESS_2");
                    break;
                case 2:
                // This response disabled due to lack of a speech file.
                // NPCPhrase (PLEADING_IS_USELESS_3);
                // break;
                case 3:
                    NPCPhrase("PLEADING_IS_USELESS_4");
                    --numVisits;
                    break;
            }
            SetGameState("KOHR_AH_PLEAD", numVisits);
        }
        else if (PlayerSaid(r, "why_kill_all_1") || PlayerSaid(r, "why_kill_all_2")
            || PlayerSaid(r, "why_kill_all_3") || PlayerSaid(r, "why_kill_all_4"))
            Counted("KOHR_AH_REASONS", "KILL_BECAUSE_1", "KILL_BECAUSE_2", "KILL_BECAUSE_3", "KILL_BECAUSE_4");
        else if (PlayerSaid(r, "please_dont_kill_1") || PlayerSaid(r, "please_dont_kill_2")
            || PlayerSaid(r, "please_dont_kill_3") || PlayerSaid(r, "please_dont_kill_4"))
            Counted("KOHR_AH_PLEAD", "WILL_KILL_1", "WILL_KILL_2", "WILL_KILL_3", "WILL_KILL_4");
        else if (PlayerSaid(r, "bye_frenzy_1") || PlayerSaid(r, "bye_frenzy_2")
            || PlayerSaid(r, "bye_frenzy_3") || PlayerSaid(r, "bye_frenzy_4"))
            Counted("KOHR_AH_INFO", "GOODBYE_AND_DIE_FRENZY_1", "GOODBYE_AND_DIE_FRENZY_2",
                "GOODBYE_AND_DIE_FRENZY_3", "GOODBYE_AND_DIE_FRENZY_4");
    }

    private void Frenzy(int r)
    {
        switch (GetGameState("KOHR_AH_REASONS"))
        {
            case 0:
                Response("why_kill_all_1", CombatIsInevitable);
                break;
            case 1:
                Response("why_kill_all_2", CombatIsInevitable);
                break;
            case 2:
                Response("why_kill_all_3", CombatIsInevitable);
                break;
            case 3:
                Response("why_kill_all_4", CombatIsInevitable);
                break;
        }
        switch (GetGameState("KOHR_AH_PLEAD"))
        {
            case 0:
                Response("please_dont_kill_1", CombatIsInevitable);
                break;
            case 1:
                Response("please_dont_kill_2", CombatIsInevitable);
                break;
            case 2:
                Response("please_dont_kill_3", CombatIsInevitable);
                break;
            case 3:
                Response("please_dont_kill_4", CombatIsInevitable);
                break;
        }
        switch (GetGameState("KOHR_AH_INFO"))
        {
            case 0:
                Response("bye_frenzy_1", CombatIsInevitable);
                break;
            case 1:
                Response("bye_frenzy_2", CombatIsInevitable);
                break;
            case 2:
                Response("bye_frenzy_3", CombatIsInevitable);
                break;
            case 3:
                Response("bye_frenzy_4", CombatIsInevitable);
                break;
        }
    }

    private void KohrAhStory(int r)
    {
        if (PlayerSaid(r, "key_phrase"))
        {
            NPCPhrase("RESPONSE_TO_KEY_PHRASE");

            SetGameState("KNOW_KOHR_AH_STORY", 2);
        }
        else if (PlayerSaid(r, "why_do_you_destroy"))
        {
            NPCPhrase("WE_WERE_SLAVES");

            DisablePhrase("why_do_you_destroy");
        }
        else if (PlayerSaid(r, "relationship_with_urquan"))
        {
            NPCPhrase("WE_ARE_URQUAN_TOO");

            DisablePhrase("relationship_with_urquan");
        }
        else if (PlayerSaid(r, "what_about_culture"))
        {
            NPCPhrase("BONE_GARDENS");

            DisablePhrase("what_about_culture");
        }
        else if (PlayerSaid(r, "how_leave_me_alone"))
        {
            NPCPhrase("YOU_DIE");

            DisablePhrase("how_leave_me_alone");
        }

        if (PhraseEnabled("why_do_you_destroy"))
            Response("why_do_you_destroy", KohrAhStory);
        if (PhraseEnabled("relationship_with_urquan"))
            Response("relationship_with_urquan", KohrAhStory);
        if (PhraseEnabled("what_about_culture"))
            Response("what_about_culture", KohrAhStory);
        if (PhraseEnabled("how_leave_me_alone"))
            Response("how_leave_me_alone", KohrAhStory);
        Response("guess_thats_all", CombatIsInevitable);
    }

    private void DieHuman(int r)
    {
        switch (GetGameState("KOHR_AH_REASONS"))
        {
            case 0:
                Response("threat_1", CombatIsInevitable);
                break;
            case 1:
                Response("threat_2", CombatIsInevitable);
                break;
            case 2:
                Response("threat_3", CombatIsInevitable);
                break;
            case 3:
                Response("threat_4", CombatIsInevitable);
                break;
        }
        if (GetGameState("KNOW_KOHR_AH_STORY") == 1)
            Response("key_phrase", KohrAhStory);
        switch (GetGameState("KOHR_AH_INFO"))
        {
            case 0:
                Response("what_are_you_hovering_over", CombatIsInevitable);
                break;
            case 1:
                Response("you_sure_are_creepy", CombatIsInevitable);
                break;
            case 2:
                Response("stop_that_gross_blinking", CombatIsInevitable);
                break;
        }
        switch (GetGameState("KOHR_AH_PLEAD"))
        {
            case 0:
                Response("plead_1", CombatIsInevitable);
                break;
            case 1:
                Response("plead_2", CombatIsInevitable);
                break;
            case 2:
            // This response disabled due to lack of a speech file.
            // Response (plead_3, CombatIsInevitable);
            // break;
            case 3:
                Response("plead_4", CombatIsInevitable);
                break;
        }
        Response("bye", CombatIsInevitable);
    }

    protected internal override void Intro()
    {
        if (T.Activity == TalkActivity.WonLastBattle)
        {
            NPCPhrase("OUT_TAKES");

            T.SetSegue(Segue.Peace);
            return;
        }

        if (GetGameState("KOHR_AH_KILLED_ALL") != 0)
        {
            NPCPhrase("GAME_OVER_DUDE");

            T.SetSegue(Segue.Peace);
            return;
        }

        if (GetGameState("KOHR_AH_SENSES_EVIL") == 0 && GetGameState("TALKING_PET_ON_SHIP") != 0)
        {
            NPCPhrase("SENSE_EVIL");
            SetGameState("KOHR_AH_SENSES_EVIL", 1);
        }

        if (T.Activity == TalkActivity.InInterplanetary && T.BattleGroupIs("SAMATRA_GRPOFFS0"))
        {
            NPCPhrase("HELLO_SAMATRA");

            SetGameState("AWARE_OF_SAMATRA", 1);
            T.SetSegue(Segue.Hostile);
        }
        else
        {
            if (GetGameState("KOHR_AH_FRENZY") != 0)
            {
                Counted("KOHR_AH_VISITS", "WE_KILL_ALL_1", "WE_KILL_ALL_2", "WE_KILL_ALL_3", "WE_KILL_ALL_4");
                Frenzy(0);
            }
            else
            {
                Counted("KOHR_AH_VISITS", "HELLO_AND_DIE_1", "HELLO_AND_DIE_2", "HELLO_AND_DIE_3", "HELLO_AND_DIE_4");
                DieHuman(0);
            }
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if (GetGameState("KOHR_AH_KILLED_ALL") == 0 && T.Activity != TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Hostile);
        else
            T.SetSegue(Segue.Peace);
    }
}
