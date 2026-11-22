// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/urquan/urquanc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Ur-Quan's talk (urquanc.c), whole: the Ur-Quan met in space, the
/// drone orbiting Earth (URQUAN_DRONE_CONVERSATION runs this script; met
/// as Sol's own group in a system it gives its message,
/// PROBE_MESSAGE_DELIVERED), the colony's guard, the Sa-Matra's, the
/// captain hypnotised by the Talking Pet, the Kohr-Ah's frenzy and the
/// endgame's won battle. Every path is checked against UQM's own script
/// (tests/golden/talks-urquan.json). Surrendering ends the talk in defeat
/// (Segue_defeat), which UQM's game ends with; the world decides what it
/// does (GameWorld.Meetings.cs): the host's flagship surrendering is the
/// game over, a guest's ship a wreck where it is (differences.md row 73).
/// </summary>
public sealed class UrquanScript : CommScript
{
    public UrquanScript(ConversationScript phrases) : base(phrases)
    {
    }

    private void CombatIsInevitable(int r)
    {
        T.SetSegue(Segue.Hostile);

        if (PlayerSaid(r, "you_must_surrender"))
            NPCPhrase("NOPE");
        else if (PlayerSaid(r, "whats_up_1") || PlayerSaid(r, "whats_up_2")
            || PlayerSaid(r, "whats_up_3") || PlayerSaid(r, "whats_up_4"))
        {
            int numVisits = GetGameState("URQUAN_INFO");
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
                    break;
                case 3:
                    NPCPhrase("GENERAL_INFO_4");
                    --numVisits;
                    break;
            }
            SetGameState("URQUAN_INFO", numVisits);
        }
        else if (PlayerSaid(r, "i_wont_surrender"))
            NPCPhrase("BAD_CHOICE");
        else if (PlayerSaid(r, "i_will_surrender"))
        {
            NPCPhrase("GOOD_CHOICE");

            T.SetSegue(Segue.Defeat);
        }
        else if (PlayerSaid(r, "like_to_leave"))
            NPCPhrase("INDEPENDENCE_IS_BAD");
        else if (PlayerSaid(r, "bye_wars_over"))
        {
            NPCPhrase("GOODBYE_WARS_OVER");

            T.SetSegue(Segue.Peace);
        }
        else if (PlayerSaid(r, "bye_sub_hypno"))
            NPCPhrase("GOODBYE_AND_DIE_SUB_HYPNO");
        else if (PlayerSaid(r, "bye_init_hypno"))
        {
            NPCPhrase("GOODBYE_AND_DIE_INIT_HYPNO");

            SetGameState("URQUAN_HYPNO_VISITS", 1);
        }
        else if (PlayerSaid(r, "terran_amphibian") || PlayerSaid(r, "talking_pet_on_steroids"))
        {
            NPCPhrase("BAD_NEWS");

            T.SetSegue(Segue.Peace);
            SetGameState("URQUAN_HYPNO_VISITS", 1);
        }
        else if (PlayerSaid(r, "falling_asleep") || PlayerSaid(r, "someone_else_controlled"))
        {
            NPCPhrase("SOUNDS_FAMILIAR");

            T.SetSegue(Segue.Peace);
            SetGameState("URQUAN_HYPNO_VISITS", 1);
        }
    }

    private void DescribePet(int r)
    {
        if (PlayerSaid(r, "wascally_little_guy"))
            NPCPhrase("WHAT_IT_LOOK_LIKE");
        else if (PlayerSaid(r, "turd_and_toad"))
        {
            NPCPhrase("WHAT_IS_TURD_AND_TOAD");

            DisablePhrase("turd_and_toad");
        }

        Response("terran_amphibian", CombatIsInevitable);
        Response("talking_pet_on_steroids", CombatIsInevitable);
        if (PhraseEnabled("turd_and_toad"))
            Response("turd_and_toad", DescribePet);
    }

    private void DescribeCompulsion(int r)
    {
        if (PlayerSaid(r, "alien_mind_control"))
            NPCPhrase("WHAT_FELT_LIKE");
        else if (PlayerSaid(r, "before_coffee"))
        {
            NPCPhrase("EXPLAIN");

            DisablePhrase("before_coffee");
        }

        Response("falling_asleep", CombatIsInevitable);
        Response("someone_else_controlled", CombatIsInevitable);
        if (PhraseEnabled("before_coffee"))
            Response("before_coffee", DescribeCompulsion);
    }

    private void MentionCompulsion(int r)
    {
        if (PlayerSaid(r, "compulsion"))
        {
            NPCPhrase("WHAT_COMPULSION");

            SetGameState("MENTIONED_PET_COMPULSION", 1);
        }
        else if (PlayerSaid(r, "possessed_by_devil"))
        {
            NPCPhrase("STUPID_DEVIL");

            DisablePhrase("possessed_by_devil");
        }
        else if (PlayerSaid(r, "why_explain"))
        {
            NPCPhrase("MUST_EXPLAIN");

            DisablePhrase("why_explain");
        }

        Response("wascally_little_guy", DescribePet);
        Response("alien_mind_control", DescribeCompulsion);
        if (PhraseEnabled("possessed_by_devil"))
            Response("possessed_by_devil", MentionCompulsion);
        if (PhraseEnabled("why_explain"))
            Response("why_explain", MentionCompulsion);
    }

    private void UrquanHypno(int r)
    {
        if (GetGameState("URQUAN_HYPNO_VISITS") == 0)
        {
            if (r == 0)
                NPCPhrase("INIT_URQUAN_WAKE_UP");
            else if (PlayerSaid(r, "where_am_i"))
            {
                NPCPhrase("YOU_ARE_HERE");

                DisablePhrase("where_am_i");
            }
            else if (PlayerSaid(r, "why_does_my_head_hurt"))
            {
                NPCPhrase("HURTS_BECAUSE");

                DisablePhrase("why_does_my_head_hurt");
            }
            else if (PlayerSaid(r, "what_about_2_weeks"))
            {
                NPCPhrase("ABOUT_2_WEEKS");

                DisablePhrase("what_about_2_weeks");
            }

            if (PhraseEnabled("where_am_i"))
                Response("where_am_i", UrquanHypno);
            if (PhraseEnabled("why_does_my_head_hurt"))
                Response("why_does_my_head_hurt", UrquanHypno);
            if (PhraseEnabled("what_about_2_weeks"))
                Response("what_about_2_weeks", UrquanHypno);
            Response("compulsion", MentionCompulsion);
            Response("bye_init_hypno", CombatIsInevitable);
        }
        else
        {
            if (r == 0)
                NPCPhrase("SUBSEQUENT_URQUAN_WAKE_UP");
            else if (PlayerSaid(r, "uh_oh"))
            {
                NPCPhrase("NO_UH_OH");

                DisablePhrase("uh_oh");
            }
            else if (PlayerSaid(r, "stop_meeting"))
            {
                NPCPhrase("NO_STOP_MEETING");

                DisablePhrase("stop_meeting");
            }

            if (PhraseEnabled("uh_oh"))
                Response("uh_oh", UrquanHypno);
            if (PhraseEnabled("stop_meeting"))
                Response("stop_meeting", UrquanHypno);
            if (GetGameState("MENTIONED_PET_COMPULSION") == 0)
                Response("compulsion", MentionCompulsion);
            Response("bye_sub_hypno", CombatIsInevitable);
        }
    }

    private void NormalUrquan(int r)
    {
        if (PlayerSaid(r, "i_surrender"))
        {
            NPCPhrase("DISOBEDIENCE_PUNISHED");

            Response("i_wont_surrender", CombatIsInevitable);
            Response("i_will_surrender", CombatIsInevitable);
        }
        else
        {
            if (PlayerSaid(r, "key_phrase"))
            {
                NPCPhrase("URQUAN_STORY");

                SetGameState("KNOW_URQUAN_STORY", 2);
            }

            Response("you_must_surrender", CombatIsInevitable);
            if (GetGameState("KNOW_URQUAN_STORY") == 1)
                Response("key_phrase", NormalUrquan);
            switch (GetGameState("URQUAN_INFO"))
            {
                case 0:
                    Response("whats_up_1", CombatIsInevitable);
                    break;
                case 1:
                    Response("whats_up_2", CombatIsInevitable);
                    break;
                case 2:
                    Response("whats_up_3", CombatIsInevitable);
                    break;
                case 3:
                    Response("whats_up_4", CombatIsInevitable);
                    break;
            }
            Response("i_surrender", NormalUrquan);
            Response("like_to_leave", CombatIsInevitable);
        }
    }

    private void LoserUrquan(int r)
    {
        if (PlayerSaid(r, "why_flee"))
        {
            NPCPhrase("FLEE_BECAUSE");

            DisablePhrase("why_flee");
        }
        else if (PlayerSaid(r, "what_happens_now"))
        {
            NPCPhrase("HAPPENS_NOW");

            DisablePhrase("what_happens_now");
        }
        else if (PlayerSaid(r, "what_about_you"))
        {
            NPCPhrase("ABOUT_US");

            DisablePhrase("what_about_you");
        }

        if (PhraseEnabled("why_flee"))
            Response("why_flee", LoserUrquan);
        if (PhraseEnabled("what_happens_now"))
            Response("what_happens_now", LoserUrquan);
        if (PhraseEnabled("what_about_you"))
            Response("what_about_you", LoserUrquan);
        Response("bye_wars_over", CombatIsInevitable);
    }

    // LOBYTE (GLOBAL (CurrentActivity)) == IN_INTERPLANETARY && GLOBAL (BattleGroupRef)
    //     && GLOBAL (BattleGroupRef) == GET_GAME_STATE_32 (field)
    private bool MetInSystem(string offsetField) =>
        T.Activity == TalkActivity.InInterplanetary && T.BattleGroupIs(offsetField);

    protected internal override void Intro()
    {
        if (T.Activity == TalkActivity.WonLastBattle)
        {
            NPCPhrase("OUT_TAKES");

            T.SetSegue(Segue.Peace);
            return;
        }

        if (MetInSystem("URQUAN_PROBE_GRPOFFS0"))
        {
            NPCPhrase("SEND_MESSAGE");
            SetGameState("PROBE_MESSAGE_DELIVERED", 1);
        }
        else if (GetGameState("PLAYER_HYPNOTIZED") != 0)
        {
            T.SetCommIntroMode(CommIntroMode.FadeInScreen, Talk.OneSecond * 5);
            UrquanHypno(0);
        }
        else
        {
            if (GetGameState("URQUAN_SENSES_EVIL") == 0 && GetGameState("TALKING_PET_ON_SHIP") != 0)
            {
                NPCPhrase("SENSE_EVIL");
                SetGameState("URQUAN_SENSES_EVIL", 1);
            }

            if (MetInSystem("COLONY_GRPOFFS0"))
            {
                NPCPhrase("CAUGHT_YA");

                T.SetSegue(Segue.Hostile);
                return;
            }

            if (MetInSystem("SAMATRA_GRPOFFS0"))
            {
                NPCPhrase("HELLO_SAMATRA");

                SetGameState("AWARE_OF_SAMATRA", 1);
                T.SetSegue(Segue.Hostile);
            }
            else
            {
                int numVisits = GetGameState("URQUAN_VISITS");
                if (GetGameState("KOHR_AH_FRENZY") == 0)
                {
                    switch (numVisits++)
                    {
                        case 0:
                            NPCPhrase("INIT_HELLO");
                            break;
                        case 1:
                            NPCPhrase("SUBSEQUENT_HELLO_1");
                            break;
                        case 2:
                            NPCPhrase("SUBSEQUENT_HELLO_2");
                            break;
                        case 3:
                            NPCPhrase("SUBSEQUENT_HELLO_3");
                            break;
                        case 4:
                            NPCPhrase("SUBSEQUENT_HELLO_4");
                            --numVisits;
                            break;
                    }

                    NormalUrquan(0);
                }
                else
                {
                    switch (numVisits++)
                    {
                        case 0:
                            NPCPhrase("INIT_FLEE_HUMAN");
                            LoserUrquan(0);
                            break;
                        case 1:
                            NPCPhrase("SUBSEQUENT_FLEE_HUMAN");
                            T.SetSegue(Segue.Peace);
                            --numVisits;
                            break;
                    }
                }
                SetGameState("URQUAN_VISITS", numVisits);
            }
        }
    }

    protected internal override void PostEncounter() => SetGameState("PLAYER_HYPNOTIZED", 0);

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if (GetGameState("PLAYER_HYPNOTIZED") != 0
            || T.Activity == TalkActivity.WonLastBattle
            || MetInSystem("URQUAN_PROBE_GRPOFFS0"))
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
