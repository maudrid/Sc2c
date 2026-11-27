// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/utwig/utwigc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Utwig (utwigc.c), whole: in space, at their homeworld
/// (GLOBAL_FLAGS_AND_DATA bit 7) and at the bomb's moon (bit 6, genutw.c's
/// guard while they haven't their Ultron). Init leaves the talk hostile
/// until they have it (UTWIG_HAVE_ULTRON), so the encounter screen comes
/// first. The neutral talk in their gloom (UTWIG_STACK1 and 2,
/// UTWIG_WAR_NEWS: the names spliced into "we are", the Supox's sphere
/// shown by what_about_you_2's StartSphereTracking; what_about_urquan
/// takes what_about_you's place among the replies until UTWIG_STACK2 is 2,
/// as utwigc.c's two switches both write pStr[2]); mocked
/// (dont_be_babies) or robbed at the bomb's moon (demand_bomb) they turn
/// on the players (UTWIG_HOSTILE, their greetings counted afresh);
/// real_sorry_about_ultron ends the talk with the segue as it stands (a
/// talk hailed from the encounter screen is at peace by then, comm.c:1391).
/// The bomb's moon's questions (BOMB_STACK1 and 2, BOMB_INFO) and the
/// Ultron refused there (REFUSED_ULTRON_AT_BOMB). The Ultron shown
/// (ULTRON_CONDITION, the group's): broken or half mended it's a sick
/// trick (from a hostile greeting a fight); mended whole (4) they take it
/// (UTWIG_HAVE_ULTRON, ULTRON_CONDITION 5), every visit, news and grudge
/// of theirs and the Supox's counted afresh, both races allied
/// (SetRaceAllied), and unless the Kohr-Ah have won their war
/// (KOHR_AH_FRENZY) their mission starts (ADVANCE_UTWIG_SUPOX_MISSION,
/// due today, so run at once: since stage 3c item 3 their fleets set off). The
/// allies: at home their news by the mission's state
/// (UTWIG_SUPOX_MISSION, UTWIG_WAR_NEWS) and 4 ships while the mission
/// hasn't started (to the host's fleet, whoever answers, while it has
/// room: EscortFeasibilityStudy, AddEscortShips; offered only with room
/// and answered by the same test, so UQM never says DONT_NEED, which
/// here is said when the host's fleet fills between the two, by the
/// shipyard or another player's talk); in space before and
/// after the Kohr-Ah (UTWIG_INFO), and a greeting alone while they fight
/// them. With the Ultron back their own song (UTWIG_ULTRON_MUSIC: noted
/// for the audio stage, nothing plays). Every path is checked against
/// UQM's own script (tests/golden/talks-utwig.json).
/// </summary>
public sealed class UtwigScript : CommScript
{
    // races.h's UTWIG_SHIP and SUPOX_SHIP.
    private const int UtwigShip = 9, SupoxShip = 7;

    public UtwigScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private bool AtBomb => (GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 6)) != 0;

    private bool AtHome => (GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0;

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Peace);

        if (PlayerSaid(r, "bye_neutral"))
            NPCPhrase("GOODBYE_NEUTRAL");
        else if (PlayerSaid(r, "bye_after_space"))
            NPCPhrase("GOODBYE_AFTER_SPACE");
        else if (PlayerSaid(r, "bye_before_space"))
            NPCPhrase("GOODBYE_BEFORE_SPACE");
        else if (PlayerSaid(r, "bye_allied_homeworld"))
            NPCPhrase("GOODBYE_ALLIED_HOMEWORLD");
        else if (PlayerSaid(r, "bye_bomb"))
            NPCPhrase("GOODBYE_BOMB");
        else if (PlayerSaid(r, "demand_bomb"))
        {
            // Never reached: BombWorld answers demand_bomb itself.
            NPCPhrase("GUARDS_FIGHT");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "got_ultron") || PlayerSaid(r, "hey_wait_got_ultron"))
        {
            if (AtBomb)
            {
                NPCPhrase("NO_ULTRON_AT_BOMB");

                SetGameState("REFUSED_ULTRON_AT_BOMB", 1);
            }
            else
            {
                if (PlayerSaid(r, "got_ultron"))
                    NPCPhrase("DONT_WANT_TO_LOOK");
                else
                    NPCPhrase("TAUNT_US_BUT_WE_LOOK");
                if (GetGameState("ULTRON_CONDITION") < 4)
                {
                    switch (GetGameState("UTWIG_INFO"))
                    {
                        case 0:
                            if (PlayerSaid(r, "got_ultron"))
                                NPCPhrase("SICK_TRICK_1");
                            else
                            {
                                NPCPhrase("TRICKED_US_1");

                                T.SetSegue(Segue.Hostile);
                            }
                            break;
                        case 1:
                            if (PlayerSaid(r, "got_ultron"))
                                NPCPhrase("SICK_TRICK_2");
                            else
                            {
                                NPCPhrase("TRICKED_US_2");

                                T.SetSegue(Segue.Hostile);
                            }
                            break;
                    }
                    SetGameState("UTWIG_INFO", 1);
                }
                else
                {
                    NPCPhrase("HAPPY_DAYS");
                    if (GetGameState("KOHR_AH_FRENZY") != 0)
                        NPCPhrase("TOO_LATE");
                    else
                    {
                        NPCPhrase("OK_ATTACK_KOHRAH");

                        // The mission's first step, due today: run at once
                        // (StoryState's, since stage 3c item 3).
                        T.AddEvent(relative: true, 0, 0, 0, "ADVANCE_UTWIG_SUPOX_MISSION");
                    }

                    SetGameState("UTWIG_HAVE_ULTRON", 1);
                    SetGameState("ULTRON_CONDITION", 5);

                    SetGameState("UTWIG_VISITS", 0);
                    SetGameState("SUPOX_VISITS", 0);
                    SetGameState("UTWIG_HOME_VISITS", 0);
                    SetGameState("SUPOX_HOME_VISITS", 0);
                    SetGameState("BOMB_VISITS", 0);

                    SetGameState("SUPOX_INFO", 0);
                    SetGameState("UTWIG_INFO", 0);
                    SetGameState("SUPOX_WAR_NEWS", 0);
                    SetGameState("UTWIG_WAR_NEWS", 0);
                    SetGameState("SUPOX_HOSTILE", 0);
                    SetGameState("UTWIG_HOSTILE", 0);

                    T.SetRaceAllied(UtwigShip, true);
                    T.SetRaceAllied(SupoxShip, true);
                }
            }
        }
        else if (PlayerSaid(r, "can_you_help"))
        {
            NPCPhrase("HOW_HELP");
            if (Sis.EscortRoom == 0)   // EscortFeasibilityStudy (UTWIG_SHIP)
                NPCPhrase("DONT_NEED");
            else
            {
                NPCPhrase("HAVE_4_SHIPS");

                T.AlienTalkSegue(Talk.WaitAll);
                T.AddEscortShips(UtwigShip, 4);
            }
        }
    }

    private void AlliedHome(int r)
    {
        int news = GetGameState("UTWIG_WAR_NEWS");
        int numVisits = GetGameState("UTWIG_SUPOX_MISSION");
        if (PlayerSaid(r, "how_went_war"))
        {
            NPCPhrase("ABOUT_BATTLE");

            news |= 1 << 0;
        }
        else if (PlayerSaid(r, "how_goes_war"))
        {
            if (numVisits == 1)
            {
                NPCPhrase("FLEET_ON_WAY");

                // Written back over by the news as read above, below.
                SetGameState("UTWIG_WAR_NEWS", 1);
            }
            else
            {
                switch (GetGameState("UTWIG_WAR_NEWS"))
                {
                    case 0:
                        NPCPhrase("BATTLE_HAPPENS_1");
                        news = 1;
                        break;
                    case 1:
                        NPCPhrase("BATTLE_HAPPENS_2");
                        news = 2;
                        break;
                }
            }

            DisablePhrase("how_goes_war");
        }
        else if (PlayerSaid(r, "learn_new_info"))
        {
            if (numVisits < 5)
                NPCPhrase("NO_NEW_INFO");
            else
            {
                NPCPhrase("SAMATRA");

                news |= 1 << 1;
            }

            DisablePhrase("learn_new_info");
        }
        else if (PlayerSaid(r, "what_now_homeworld"))
        {
            if (numVisits < 5)
                NPCPhrase("UP_TO_YOU");
            else
                NPCPhrase("HOPE_KILL_EACH_OTHER");

            DisablePhrase("what_now_homeworld");
        }
        else if (PlayerSaid(r, "how_is_ultron"))
        {
            NPCPhrase("ULTRON_IS_GREAT");

            DisablePhrase("how_is_ultron");
        }
        SetGameState("UTWIG_WAR_NEWS", news);

        if (numVisits >= 5)
        {
            if ((news & (1 << 0)) == 0)
                Response("how_went_war", AlliedHome);
        }
        else if (PhraseEnabled("how_goes_war")
                 && ((numVisits == 1 && news == 0) || (numVisits != 0 && news < 2)))
            Response("how_goes_war", AlliedHome);
        if (PhraseEnabled("learn_new_info"))
            Response("learn_new_info", AlliedHome);
        if (PhraseEnabled("what_now_homeworld"))
            Response("what_now_homeworld", AlliedHome);
        if (PhraseEnabled("how_is_ultron"))
            Response("how_is_ultron", AlliedHome);
        if (numVisits == 0 && Sis.EscortRoom != 0)   // EscortFeasibilityStudy (UTWIG_SHIP)
            Response("can_you_help", ExitConversation);
        Response("bye_allied_homeworld", ExitConversation);
    }

    private void BeforeKohrAh(int r)
    {
        if (PlayerSaid(r, "whats_up_before_space"))
        {
            int numVisits = GetGameState("UTWIG_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_BEFORE_SPACE_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_BEFORE_SPACE_2");
                    --numVisits;
                    break;
            }
            SetGameState("UTWIG_INFO", numVisits);

            DisablePhrase("whats_up_before_space");
        }
        else if (PlayerSaid(r, "what_now_before_space"))
        {
            NPCPhrase("DO_THIS_BEFORE_SPACE");

            DisablePhrase("what_now_before_space");
        }

        if (PhraseEnabled("whats_up_before_space"))
            Response("whats_up_before_space", BeforeKohrAh);
        if (PhraseEnabled("what_now_before_space"))
            Response("what_now_before_space", BeforeKohrAh);
        Response("bye_before_space", ExitConversation);
    }

    private void AfterKohrAh(int r)
    {
        if (PlayerSaid(r, "whats_up_after_space"))
        {
            int numVisits = GetGameState("UTWIG_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_AFTER_SPACE_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_AFTER_SPACE_2");
                    --numVisits;
                    break;
            }
            SetGameState("UTWIG_INFO", numVisits);

            DisablePhrase("whats_up_after_space");
        }
        else if (PlayerSaid(r, "what_now_after_space"))
        {
            NPCPhrase("DO_THIS_AFTER_SPACE");

            DisablePhrase("what_now_after_space");
        }

        if (PhraseEnabled("whats_up_after_space"))
            Response("whats_up_after_space", AfterKohrAh);
        if (PhraseEnabled("what_now_after_space"))
            Response("what_now_after_space", AfterKohrAh);
        Response("bye_after_space", ExitConversation);
    }

    private void NeutralUtwig(int r)
    {
        int lastStack = 0;
        var pStr = new string?[4];
        if (PlayerSaid(r, "we_are_vindicator0"))
        {
            NPCPhrase("WOULD_BE_HAPPY_BUT");

            SetGameState("UTWIG_STACK1", 1);
        }
        else if (PlayerSaid(r, "why_sad"))
        {
            NPCPhrase("ULTRON_BROKE");

            SetGameState("UTWIG_STACK1", 2);
        }
        else if (PlayerSaid(r, "what_ultron"))
        {
            NPCPhrase("GLORIOUS_ULTRON");

            SetGameState("UTWIG_STACK1", 3);
        }
        else if (PlayerSaid(r, "dont_be_babies"))
        {
            NPCPhrase("MOCK_OUR_PAIN");

            T.SetSegue(Segue.Hostile);
            SetGameState("UTWIG_STACK1", 4);
            SetGameState("UTWIG_HOSTILE", 1);
            SetGameState("UTWIG_INFO", 0);
            SetGameState("UTWIG_HOME_VISITS", 0);
            SetGameState("UTWIG_VISITS", 0);
            SetGameState("BOMB_VISITS", 0);
            return;
        }
        else if (PlayerSaid(r, "real_sorry_about_ultron"))
        {
            NPCPhrase("APPRECIATE_SYMPATHY");

            // No segue set: the talk ends as it stands (init's hostile
            // one, or the peace a hail from the encounter screen made).
            SetGameState("UTWIG_STACK1", 4);
            return;
        }
        else if (PlayerSaid(r, "what_about_you_1"))
        {
            NPCPhrase("ABOUT_US_1");

            lastStack = 2;
            SetGameState("UTWIG_WAR_NEWS", 1);
        }
        else if (PlayerSaid(r, "what_about_you_2"))
        {
            NPCPhrase("ABOUT_US_2");

            lastStack = 2;
            T.StartSphereTracking(SupoxShip);
            SetGameState("UTWIG_WAR_NEWS", 2);
        }
        else if (PlayerSaid(r, "what_about_you_3"))
        {
            NPCPhrase("ABOUT_US_3");

            SetGameState("UTWIG_WAR_NEWS", 3);
        }
        else if (PlayerSaid(r, "what_about_urquan_1"))
        {
            NPCPhrase("ABOUT_URQUAN_1");

            // pStr[3] is never given a reply: nothing is offered first.
            lastStack = 3;
            SetGameState("UTWIG_STACK2", 1);
        }
        else if (PlayerSaid(r, "what_about_urquan_2"))
        {
            NPCPhrase("ABOUT_URQUAN_2");

            SetGameState("UTWIG_STACK2", 2);
        }

        switch (GetGameState("UTWIG_STACK1"))
        {
            case 0:
                T.ConstructResponse("we_are_vindicator0", Sis.CaptainName, "we_are_vindicator1",
                    T.GetAllianceName("name_1"), "we_are_vindicator2");
                pStr[0] = "we_are_vindicator0";
                break;
            case 1:
                pStr[0] = "why_sad";
                break;
            case 2:
                pStr[0] = "what_ultron";
                break;
            case 3:
                pStr[0] = "dont_be_babies";
                pStr[1] = "real_sorry_about_ultron";
                break;
        }
        switch (GetGameState("UTWIG_WAR_NEWS"))
        {
            case 0:
                pStr[2] = "what_about_you_1";
                break;
            case 1:
                pStr[2] = "what_about_you_2";
                break;
            case 2:
                pStr[2] = "what_about_you_3";
                break;
        }
        // utwigc.c writes pStr[2] here too, not pStr[3]: the Ur-Quan's
        // questions take the place of the Utwig's own until both are asked.
        switch (GetGameState("UTWIG_STACK2"))
        {
            case 0:
                pStr[2] = "what_about_urquan_1";
                break;
            case 1:
                pStr[2] = "what_about_urquan_2";
                break;
        }

        if (pStr[lastStack] is { } last)
            Offer(last);
        for (int i = 0; i < 4; ++i)
        {
            if (i != lastStack && pStr[i] is { } other)
                Offer(other);
        }
        if (GetGameState("ULTRON_CONDITION") != 0)
            Response("got_ultron", ExitConversation);
        Response("bye_neutral", ExitConversation);

        void Offer(string id)
        {
            if (id != "we_are_vindicator0")
                Response(id, NeutralUtwig);
            else
                T.DoResponsePhrase(id, NeutralUtwig, Talk.SharedPhraseBuffer);
        }
    }

    private void BombWorld(int r)
    {
        int lastStack = 0;
        var pStr = new string?[2];
        if (PlayerSaid(r, "why_you_here"))
        {
            NPCPhrase("WE_GUARD_BOMB");

            SetGameState("BOMB_STACK1", 1);
        }
        else if (PlayerSaid(r, "what_about_bomb"))
        {
            NPCPhrase("ABOUT_BOMB");

            SetGameState("BOMB_STACK1", 2);
        }
        else if (PlayerSaid(r, "give_us_bomb_or_die"))
        {
            NPCPhrase("GUARDS_WARN");

            SetGameState("BOMB_STACK1", 3);
        }
        else if (PlayerSaid(r, "demand_bomb"))
        {
            NPCPhrase("GUARDS_FIGHT");

            T.SetSegue(Segue.Hostile);
            SetGameState("UTWIG_HOSTILE", 1);
            SetGameState("UTWIG_INFO", 0);
            SetGameState("UTWIG_HOME_VISITS", 0);
            SetGameState("UTWIG_VISITS", 0);
            SetGameState("BOMB_VISITS", 0);
            return;
        }
        else if (PlayerSaid(r, "may_we_have_bomb"))
        {
            NPCPhrase("NO_BOMB");

            lastStack = 1;
            SetGameState("BOMB_STACK2", 1);
        }
        else if (PlayerSaid(r, "please"))
        {
            NPCPhrase("SORRY_NO_BOMB");

            SetGameState("BOMB_STACK2", 2);
        }
        else if (PlayerSaid(r, "whats_up_bomb"))
        {
            if (GetGameState("BOMB_INFO") != 0)
                NPCPhrase("GENERAL_INFO_BOMB_2");
            else
            {
                NPCPhrase("GENERAL_INFO_BOMB_1");

                SetGameState("BOMB_INFO", 1);
            }

            DisablePhrase("whats_up_bomb");
        }

        switch (GetGameState("BOMB_STACK2"))
        {
            case 0:
                pStr[1] = "may_we_have_bomb";
                break;
            case 1:
                pStr[1] = "please";
                break;
        }
        switch (GetGameState("BOMB_STACK1"))
        {
            case 0:
                pStr[0] = "why_you_here";
                pStr[1] = null;
                break;
            case 1:
                pStr[0] = "what_about_bomb";
                pStr[1] = null;
                break;
            case 2:
                pStr[0] = "give_us_bomb_or_die";
                break;
            case 3:
                pStr[0] = "demand_bomb";
                break;
        }

        if (pStr[lastStack] is { } first)
            Response(first, BombWorld);
        lastStack ^= 1;
        if (pStr[lastStack] is { } second)
            Response(second, BombWorld);

        if (PhraseEnabled("whats_up_bomb") && GetGameState("BOMB_STACK1") > 1)
            Response("whats_up_bomb", BombWorld);

        if (GetGameState("ULTRON_CONDITION") != 0 && GetGameState("REFUSED_ULTRON_AT_BOMB") == 0)
            Response("got_ultron", ExitConversation);

        if (GetGameState("BOMB_INFO") != 0)
            Response("bye_bomb", ExitConversation);
        else
            Response("bye_neutral", ExitConversation);
    }

    // The greetings by visit: each switch's last case keeps its count there.
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

        if (GetGameState("UTWIG_HOSTILE") != 0)
        {
            if (AtBomb)
                Greet("BOMB_VISITS", "HOSTILE_BOMB_HELLO_1", "HOSTILE_BOMB_HELLO_2");
            else if (AtHome)
                Greet("UTWIG_HOME_VISITS", "HOSTILE_HOMEWORLD_HELLO_1", "HOSTILE_HOMEWORLD_HELLO_2");
            else
                Greet("UTWIG_VISITS", "HOSTILE_SPACE_HELLO_1", "HOSTILE_SPACE_HELLO_2");

            if (GetGameState("ULTRON_CONDITION") == 0 || AtBomb)
                T.SetSegue(Segue.Hostile);
            else
                Response("hey_wait_got_ultron", ExitConversation);
        }
        else if (T.CheckAlliance(UtwigShip) == Story.Allied.Good)
        {
            if (AtHome)
            {
                Greet("UTWIG_HOME_VISITS", "ALLIED_HOMEWORLD_HELLO_1", "ALLIED_HOMEWORLD_HELLO_2",
                    "ALLIED_HOMEWORLD_HELLO_3", "ALLIED_HOMEWORLD_HELLO_4");

                AlliedHome(0);
            }
            else
            {
                int mission = GetGameState("UTWIG_SUPOX_MISSION");
                if (mission == 1)
                {
                    Greet("UTWIG_VISITS", "HELLO_BEFORE_KOHRAH_SPACE_1", "HELLO_BEFORE_KOHRAH_SPACE_2");

                    BeforeKohrAh(0);
                }
                else if (mission < 5)
                {
                    // No replies: the greeting is the whole talk.
                    Greet("UTWIG_VISITS", "HELLO_DURING_KOHRAH_SPACE_1", "HELLO_DURING_KOHRAH_SPACE_2");
                }
                else
                {
                    Greet("UTWIG_VISITS", "HELLO_AFTER_KOHRAH_SPACE_1", "HELLO_AFTER_KOHRAH_SPACE_2");

                    AfterKohrAh(0);
                }
            }
        }
        else if (AtBomb)
        {
            Greet("BOMB_VISITS", "BOMB_WORLD_HELLO_1", "BOMB_WORLD_HELLO_2");

            BombWorld(0);
        }
        else
        {
            if (AtHome)
                Greet("UTWIG_HOME_VISITS", "NEUTRAL_HOMEWORLD_HELLO_1", "NEUTRAL_HOMEWORLD_HELLO_2",
                    "NEUTRAL_HOMEWORLD_HELLO_3", "NEUTRAL_HOMEWORLD_HELLO_4");
            else
                Greet("UTWIG_VISITS", "NEUTRAL_SPACE_HELLO_1", "NEUTRAL_SPACE_HELLO_2");

            NeutralUtwig(0);
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if (GetGameState("UTWIG_HAVE_ULTRON") != 0)
        {
            // The alternate "Happy Utwig!" track, if it loads
            // (UTWIG_ULTRON_MUSIC, LDASF_USE_ALTERNATE).
            T.AltSong = "comm.utwig.ultron.music";
            T.UseAltSong = true;
        }
        else
        {
            // The regular track.
            T.UseAltSong = false;
        }

        if (GetGameState("UTWIG_HAVE_ULTRON") != 0 || T.Activity == TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
