// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/mycon/myconc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Mycon (myconc.c), whole: in space (MYCON_VISITS), at their
/// homeworld (GLOBAL_FLAGS_AND_DATA bit 7, MYCON_HOME_VISITS) and at the
/// Sun Device's world (bit 6, MYCON_SUN_VISITS; genmyc.c's guard), hostile
/// from the start unless the endgame's battle is won. In space and at
/// home each ordinary reply (not the farewells, the shattered worlds'
/// question or the trick's) is answered with the next of their 32
/// rambles (MYCON_RAMBLE),
/// and whether a reply goes on with the talk or ends it in a fight is
/// drawn afresh each time the replies are offered (TFB_Random: the talk's
/// own generator, 30 in 100 a fight); one insult (in space,
/// MYCON_INSULTS) or question (at home, MYCON_INFO) a talk. At home: the
/// shattered worlds asked about once the players have seen one
/// (KNOW_ABOUT_SHATTERED), and, once the Syreen have set their ambush
/// (MYCON_AMBUSH, 3c's), the trick: the three clues, after which the Mycon
/// leave in peace to be ambushed (MYCON_FELL_FOR_AMBUSH and the
/// ADVANCE_MYCON_MISSION event, run at once since stage 3c item 3). At the Sun Device's world: their news, the trick refused
/// there (NO_TRICK_AT_SUN), the implanting asked about once they fell for
/// it, landing refused (a fight) and the one peaceful farewell. With the
/// Sun Device taken (SUN_DEVICE) or the ambush known (MYCON_KNOW_AMBUSH)
/// they only threaten. Every path is checked against UQM's own script
/// (tests/golden/talks-mycon.json).
/// </summary>
public sealed class MyconScript : CommScript
{
    // The script's static: whether this talk's insult or question has been made.
    private bool _madeChoice;

    public MyconScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private bool AtHomeworld => (GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0;

    private void DoRamble(int r)
    {
        int counter = GetGameState("MYCON_RAMBLE");
        switch (counter++)
        {
            case 0:
                NPCPhrase("RAMBLE_1");
                break;
            case 1:
                NPCPhrase("RAMBLE_2");
                break;
            case 2:
                NPCPhrase("RAMBLE_3");
                break;
            case 3:
                NPCPhrase("RAMBLE_4");
                break;
            case 4:
                NPCPhrase("RAMBLE_5");
                break;
            case 5:
                NPCPhrase("RAMBLE_6");
                break;
            case 6:
                NPCPhrase("RAMBLE_7");
                break;
            case 7:
                NPCPhrase("RAMBLE_8");
                break;
            case 8:
                NPCPhrase("RAMBLE_9");
                break;
            case 9:
                NPCPhrase("RAMBLE_10");
                break;
            case 10:
                NPCPhrase("RAMBLE_11");
                break;
            case 11:
                NPCPhrase("RAMBLE_12");
                break;
            case 12:
                NPCPhrase("RAMBLE_13");
                break;
            case 13:
                NPCPhrase("RAMBLE_14");
                break;
            case 14:
                NPCPhrase("RAMBLE_15");
                break;
            case 15:
                NPCPhrase("RAMBLE_16");
                break;
            case 16:
                NPCPhrase("RAMBLE_17");
                break;
            case 17:
                NPCPhrase("RAMBLE_18");
                break;
            case 18:
                NPCPhrase("RAMBLE_19");
                break;
            case 19:
                NPCPhrase("RAMBLE_20");
                break;
            case 20:
                NPCPhrase("RAMBLE_21");
                break;
            case 21:
                NPCPhrase("RAMBLE_22");
                break;
            case 22:
                NPCPhrase("RAMBLE_23");
                break;
            case 23:
                NPCPhrase("RAMBLE_24");
                break;
            case 24:
                NPCPhrase("RAMBLE_25");
                if (GetGameState("KNOW_ABOUT_SHATTERED") < 2)
                    SetGameState("KNOW_ABOUT_SHATTERED", 2);
                break;
            case 25:
                NPCPhrase("RAMBLE_26");
                break;
            case 26:
                NPCPhrase("RAMBLE_27");
                break;
            case 27:
                NPCPhrase("RAMBLE_28");
                break;
            case 28:
                NPCPhrase("RAMBLE_29");
                break;
            case 29:
                NPCPhrase("RAMBLE_30");
                break;
            case 30:
                NPCPhrase("RAMBLE_31");
                break;
            case 31:
                NPCPhrase("RAMBLE_32");
                counter = 0;
                break;
        }
        SetGameState("MYCON_RAMBLE", counter);

        if (!AtHomeworld)
        {
            if (!PlayerSaid(r, "come_in_peace") && !PlayerSaid(r, "gonna_die"))
            {
                counter = (GetGameState("MYCON_INSULTS") + 1) & 7;
                SetGameState("MYCON_INSULTS", counter);
                _madeChoice = true;
            }
        }
        else if (!PlayerSaid(r, "lets_be_friends") && !PlayerSaid(r, "came_to_homeworld")
                 && !PlayerSaid(r, "submit_to_us"))
        {
            counter = (GetGameState("MYCON_INFO") + 1) & 15;
            SetGameState("MYCON_INFO", counter);
            _madeChoice = true;
        }
    }

    private void CombatIsInevitable(int r)
    {
        T.SetSegue(Segue.Hostile);

        if (PlayerSaid(r, "bye_space"))
            NPCPhrase("BYE_AND_DIE_SPACE");
        else if (PlayerSaid(r, "bye_homeworld"))
            NPCPhrase("BYE_AND_DIE_HOMEWORLD");
        else if (PlayerSaid(r, "like_to_land"))
            NPCPhrase("NEVER_LET_LAND");
        else if (PlayerSaid(r, "bye_sun_device"))
        {
            NPCPhrase("GOODBYE_SUN_DEVICE");

            T.SetSegue(Segue.Peace);
        }
        else
        {
            DoRamble(r);
            if (!AtHomeworld)
                NPCPhrase("BYE_AND_DIE_SPACE");
            else
                NPCPhrase("BYE_AND_DIE_HOMEWORLD");
        }
        _madeChoice = false;
    }

    private void SunDevice(int r)
    {
        if (PlayerSaid(r, "whats_up_sun_device"))
        {
            NPCPhrase("GENERAL_INFO_SUN_DEVICE");

            DisablePhrase("whats_up_sun_device");
        }
        else if (PlayerSaid(r, "how_goes_implanting"))
        {
            NPCPhrase("UNFORSEEN_DELAYS");

            DisablePhrase("how_goes_implanting");
        }
        else if (PlayerSaid(r, "i_have_a_cunning_plan"))
        {
            NPCPhrase("WONT_FALL_FOR_TRICK");

            SetGameState("NO_TRICK_AT_SUN", 1);
        }

        if (PhraseEnabled("whats_up_sun_device"))
            Response("whats_up_sun_device", SunDevice);
        if (GetGameState("MYCON_FELL_FOR_AMBUSH") != 0)
        {
            if (PhraseEnabled("how_goes_implanting") && GetGameState("MYCON_FELL_FOR_AMBUSH") != 0)
                Response("how_goes_implanting", SunDevice);
            Response("like_to_land", CombatIsInevitable);
        }
        else if (GetGameState("MYCON_AMBUSH") != 0 && GetGameState("NO_TRICK_AT_SUN") == 0)
            Response("i_have_a_cunning_plan", SunDevice);
        Response("bye_sun_device", CombatIsInevitable);
    }

    private void TrickMycon(int r)
    {
        if (PlayerSaid(r, "i_have_a_cunning_plan"))
        {
            NPCPhrase("TELL_US_ABOUT_WORLD");

            DisablePhrase("i_have_a_cunning_plan");
        }
        else if (PlayerSaid(r, "clue_1"))
        {
            NPCPhrase("RESPONSE_1");

            DisablePhrase("clue_1");
        }
        else if (PlayerSaid(r, "clue_2"))
        {
            NPCPhrase("RESPONSE_2");

            DisablePhrase("clue_2");
        }
        else if (PlayerSaid(r, "clue_3"))
        {
            NPCPhrase("RESPONSE_3");

            DisablePhrase("clue_3");
        }

        if (!PhraseEnabled("clue_1") && !PhraseEnabled("clue_2") && !PhraseEnabled("clue_3"))
        {
            NPCPhrase("WE_GO_TO_IMPLANT");

            T.SetSegue(Segue.Peace);
            SetGameState("MYCON_FELL_FOR_AMBUSH", 1);
            // Their flight to the ambush, run at once (since stage 3c item 3).
            T.AddEvent(relative: true, 0, 0, 0, "ADVANCE_MYCON_MISSION");
        }
        else
            NPCPhrase("AMBUSH_TAIL");

        if (PhraseEnabled("clue_1"))
            Response("clue_1", TrickMycon);
        if (PhraseEnabled("clue_2"))
            Response("clue_2", TrickMycon);
        if (PhraseEnabled("clue_3"))
            Response("clue_3", TrickMycon);
    }

    private void NormalMycon(int r)
    {
        if (PlayerSaid(r, "what_about_shattered"))
        {
            NPCPhrase("ABOUT_SHATTERED");

            SetGameState("KNOW_ABOUT_SHATTERED", 2);
        }
        else if (r != 0)
        {
            DoRamble(r);
            NPCPhrase("RAMBLE_TAIL");

            DisablePhrase(r);
        }

        // Whether the next reply ends the talk in a fight, drawn afresh: 30 in 100.
        Action<int> respFunc = (byte)T.Random() < 256 * 30 / 100 ? CombatIsInevitable : NormalMycon;
        if (!AtHomeworld)
        {
            if (PhraseEnabled("come_in_peace"))
                Response("come_in_peace", respFunc);
            if (PhraseEnabled("gonna_die"))
                Response("gonna_die", respFunc);
            if (!_madeChoice)
                Response($"insult_{GetGameState("MYCON_INSULTS") + 1}", respFunc);   // insult_1 to insult_8
            Response("bye_space", CombatIsInevitable);
        }
        else
        {
            if (!_madeChoice)
                Response($"question_{GetGameState("MYCON_INFO") + 1}", respFunc);   // question_1 to question_16
            if (PhraseEnabled("lets_be_friends"))
                Response("lets_be_friends", respFunc);
            if (PhraseEnabled("came_to_homeworld"))
                Response("came_to_homeworld", respFunc);
            if (PhraseEnabled("submit_to_us"))
                Response("submit_to_us", respFunc);
            if (GetGameState("MYCON_FELL_FOR_AMBUSH") == 0)
            {
                if (GetGameState("KNOW_ABOUT_SHATTERED") == 1)
                    Response("what_about_shattered", NormalMycon);
                if (GetGameState("MYCON_AMBUSH") != 0)
                    Response("i_have_a_cunning_plan", TrickMycon);
            }
            Response("bye_homeworld", CombatIsInevitable);
        }
    }

    // A greeting that counts its visits: each line in turn, the last from then on.
    private void Greet(string field, params string[] lines)
    {
        int numVisits = GetGameState(field);
        int n = numVisits++;
        if (n < lines.Length)
        {
            NPCPhrase(lines[n]);
            if (n == lines.Length - 1)
                --numVisits;
        }
        SetGameState(field, numVisits);
    }

    private static readonly string[] HomeworldHellos = Enumerable.Range(1, 8).Select(i => $"HELLO_HOMEWORLD_{i}").ToArray();
    private static readonly string[] SpaceHellos = Enumerable.Range(1, 8).Select(i => $"HELLO_SPACE_{i}").ToArray();

    protected internal override void Intro()
    {
        if (GetGameState("SUN_DEVICE") != 0)
        {
            Greet("MYCON_VISITS", "DIE_THIEF", "DIE_THIEF_AGAIN");

            T.SetSegue(Segue.Hostile);
        }
        else if (GetGameState("MYCON_KNOW_AMBUSH") != 0)
        {
            NPCPhrase("DIE_LIAR");

            T.SetSegue(Segue.Hostile);
        }
        else if ((GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 6)) != 0)
        {
            Greet("MYCON_SUN_VISITS", "HELLO_SUN_DEVICE_WORLD_1", "HELLO_SUN_DEVICE_WORLD_2");

            SunDevice(0);
        }
        else if (AtHomeworld)
        {
            Greet("MYCON_HOME_VISITS", HomeworldHellos);

            NormalMycon(0);
        }
        else
        {
            Greet("MYCON_VISITS", SpaceHellos);

            NormalMycon(0);
        }
    }

    // post_mycon_enc: nothing defined.

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        _madeChoice = false;

        if (T.Activity != TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Hostile);
        else
            T.SetSegue(Segue.Peace);
    }
}
