// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/shofixt/shofixt.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Shofixti (shofixt.c), whole: Tanaka in his damaged scout, or his
/// brother Katana once Tanaka is dead (SHOFIXTI_KIA), met at the Shofixti's
/// system (genshof.c's own group). Hostile until the third meeting: each
/// visit's greeting (SHOFIXTI_VISITS), the stacks of replies that end in a
/// fight (SHOFIXTI_STACK1 to 3, the "don't want to fight" counter in
/// STACK4), and the insults that win his friendship (STACK2: two fights he
/// survives, then "donkey breath"), with the captain's, the alliance's and
/// the ship's names spliced into the reply that follows. Friendly after:
/// his reports and questions (STACK1 and STACK3), and with the Maidens
/// aboard (MAIDENS_ON_SHIP, 3c's pickup) his recruitment
/// (SHOFIXTI_RECRUITED; the return event it queues, SHOFIXTI_RETURN_EVENT,
/// set aside until stage 3c). Every path is checked against UQM's own
/// script (tests/golden/talks-shofixt.json).
/// </summary>
public sealed class ShofixtiScript : CommScript
{
    // shofixt.c's static buffers for the friendly replies with his name (buf0, buf1).
    private readonly PhraseBuffer _buf0 = new(), _buf1 = new();

    // shofixti_name: "tanaka", or "katana" once Tanaka is dead (GetShofixtiName).
    private string _shofixtiName = "tanaka";

    public ShofixtiScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private void GetShofixtiName() =>
        _shofixtiName = GetGameState("SHOFIXTI_KIA") != 0 ? "katana" : "tanaka";

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Hostile);

        if (PlayerSaid(r, "bye0"))
        {
            NPCPhrase("GOODBYE");

            T.SetSegue(Segue.Peace);
        }
        else if (PlayerSaid(r, "go_ahead"))
        {
            NPCPhrase("ON_SECOND_THOUGHT");

            T.SetSegue(Segue.Peace);
        }
        else if (PlayerSaid(r, "need_you_for_duty"))
        {
            NPCPhrase("OK_WILL_BE_SENTRY");

            T.SetSegue(Segue.Peace);
        }
        else if (PlayerSaid(r, "females") || PlayerSaid(r, "nubiles") || PlayerSaid(r, "rat_babes"))
        {
            NPCPhrase("LEAPING_HAPPINESS");

            SetGameState("SHOFIXTI_RECRUITED", 1);
            SetGameState("MAIDENS_ON_SHIP", 0);
            T.SetSegue(Segue.Peace);

            // The Shofixti's return: set aside until stage 3c runs the calendar.
            T.AddEvent(relative: true, 2, 0, 0, "SHOFIXTI_RETURN_EVENT");
        }
        else if (PlayerSaid(r, "dont_attack"))
        {
            NPCPhrase("TYPICAL_PLOY");

            SetGameState("SHOFIXTI_STACK1", 1);
        }
        else if (PlayerSaid(r, "hey_stop"))
        {
            NPCPhrase("ONLY_STOP");

            SetGameState("SHOFIXTI_STACK1", 2);
        }
        else if (PlayerSaid(r, "look_you_are"))
        {
            NPCPhrase("TOO_BAD");

            SetGameState("SHOFIXTI_STACK1", 3);
        }
        else if (PlayerSaid(r, "no_one_insults"))
        {
            NPCPhrase("YOU_LIMP");

            SetGameState("SHOFIXTI_STACK2", 1);
        }
        else if (PlayerSaid(r, "mighty_words"))
        {
            NPCPhrase("HANG_YOUR");

            SetGameState("SHOFIXTI_STACK2", 2);
        }
        else if (PlayerSaid(r, "dont_know"))
        {
            NPCPhrase("NEVER");

            SetGameState("SHOFIXTI_STACK3", 1);
        }
        else if (PlayerSaid(r, "look0"))
        {
            NPCPhrase("FOR_YOU");

            SetGameState("SHOFIXTI_STACK3", 2);
        }
        else if (PlayerSaid(r, "no_bloodshed"))
        {
            NPCPhrase("YES_BLOODSHED");

            SetGameState("SHOFIXTI_STACK3", 3);
        }
        else if (PlayerSaid(r, "dont_want_to_fight"))
        {
            int numVisits = GetGameState("SHOFIXTI_STACK4");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("MUST_FIGHT_YOU_URQUAN_1");
                    break;
                case 1:
                    NPCPhrase("MUST_FIGHT_YOU_URQUAN_2");
                    break;
                case 2:
                    NPCPhrase("MUST_FIGHT_YOU_URQUAN_3");
                    break;
                case 3:
                    NPCPhrase("MUST_FIGHT_YOU_URQUAN_4");
                    --numVisits;
                    break;
            }
            SetGameState("SHOFIXTI_STACK4", numVisits);
        }
    }

    private void GiveMaidens(int r)
    {
        if (PlayerSaid(r, "important_duty"))
        {
            NPCPhrase("WHAT_DUTY");

            Response("procreating_wildly", GiveMaidens);
            Response("replenishing_your_species", GiveMaidens);
            Response("hope_you_have", GiveMaidens);
        }
        else
        {
            NPCPhrase("SOUNDS_GREAT_BUT_HOW");

            Response("females", ExitConversation);
            Response("nubiles", ExitConversation);
            Response("rat_babes", ExitConversation);
        }
    }

    private void ConsoleShofixti(int r)
    {
        if (PlayerSaid(r, "dont_do_it"))
        {
            NPCPhrase("YES_I_DO_IT");
            DisablePhrase("dont_do_it");
        }
        else
            NPCPhrase("VERY_SAD_KILL_SELF");

        if (GetGameState("MAIDENS_ON_SHIP") != 0)
            Response("important_duty", GiveMaidens);
        if (PhraseEnabled("dont_do_it"))
            Response("dont_do_it", ConsoleShofixti);
        Response("need_you_for_duty", ExitConversation);
        Response("go_ahead", ExitConversation);
    }

    private void ExplainDefeat(int r)
    {
        if (PlayerSaid(r, "i_am_nice"))
            NPCPhrase("MUST_UNDERSTAND");
        else if (PlayerSaid(r, "i_am_guy"))
            NPCPhrase("NICE_BUT_WHAT_IS_DONKEY");
        else
            NPCPhrase("SO_SORRY");
        NPCPhrase("IS_DEFEAT_TRUE");

        Response("yes_and_no", ConsoleShofixti);
        Response("clobbered", ConsoleShofixti);
        Response("butt_blasted", ConsoleShofixti);
    }

    private void RealizeMistake(int r)
    {
        NPCPhrase("DGRUNTI");
        SetGameState("SHOFIXTI_STACK1", 0);
        SetGameState("SHOFIXTI_STACK3", 0);
        SetGameState("SHOFIXTI_STACK2", 3);

        T.ConstructResponse("i_am_captain0", Sis.CaptainName, "i_am_captain1", T.GetAllianceName("name_1"),
            "i_am_captain2", Sis.ShipName, "i_am_captain3");
        T.DoResponsePhrase("i_am_captain0", ExplainDefeat, Talk.SharedPhraseBuffer);
        Response("i_am_nice", ExplainDefeat);
        Response("i_am_guy", ExplainDefeat);
    }

    private void Hostile(int r)
    {
        switch (GetGameState("SHOFIXTI_STACK1"))
        {
            case 0:
                Response("dont_attack", ExitConversation);
                break;
            case 1:
                Response("hey_stop", ExitConversation);
                break;
            case 2:
                Response("look_you_are", ExitConversation);
                break;
        }
        switch (GetGameState("SHOFIXTI_STACK2"))
        {
            case 0:
                Response("no_one_insults", ExitConversation);
                break;
            case 1:
                Response("mighty_words", ExitConversation);
                break;
            case 2:
                Response("donkey_breath", RealizeMistake);
                break;
        }
        switch (GetGameState("SHOFIXTI_STACK3"))
        {
            case 0:
                Response("dont_know", ExitConversation);
                break;
            case 1:
                T.ConstructResponse("look0", "", _shofixtiName, "", "look1");
                T.DoResponsePhrase("look0", ExitConversation, Talk.SharedPhraseBuffer);
                break;
            case 2:
                Response("look_you_are", ExitConversation);
                break;
        }
        Response("dont_want_to_fight", ExitConversation);
    }

    private void Friendly(int r)
    {
        int lastStack = 0;
        var resp = new (string? Str, PhraseBuffer? Buf)[3];
        if (PlayerSaid(r, "report0"))
        {
            NPCPhrase("NOTHING_NEW");

            DisablePhrase("report0");
        }
        else if (PlayerSaid(r, "why_here0"))
        {
            NPCPhrase("I_GUARD");

            lastStack = 1;
            SetGameState("SHOFIXTI_STACK1", 1);
        }
        else if (PlayerSaid(r, "what_happened"))
        {
            NPCPhrase("MET_VUX");

            lastStack = 1;
            SetGameState("SHOFIXTI_STACK1", 2);
        }
        else if (PlayerSaid(r, "glory_device"))
        {
            NPCPhrase("SWITCH_BROKE");

            SetGameState("SHOFIXTI_STACK1", 3);
        }
        else if (PlayerSaid(r, "where_world"))
        {
            NPCPhrase("BLEW_IT_UP");

            lastStack = 2;
            SetGameState("SHOFIXTI_STACK3", 1);
        }
        else if (PlayerSaid(r, "how_survive"))
        {
            NPCPhrase("NOT_HERE");

            SetGameState("SHOFIXTI_STACK3", 2);
        }

        if (PhraseEnabled("report0"))
        {
            T.ConstructResponse(_buf0, "report0", "", _shofixtiName, "", "report1");
            resp[0] = ("report0", _buf0);
        }

        switch (GetGameState("SHOFIXTI_STACK1"))
        {
            case 0:
                T.ConstructResponse(_buf1, "why_here0", "", _shofixtiName, "", "why_here1");
                resp[1] = ("why_here0", _buf1);
                break;
            case 1:
                resp[1] = ("what_happened", null);
                break;
            case 2:
                resp[1] = ("glory_device", null);
                break;
        }

        switch (GetGameState("SHOFIXTI_STACK3"))
        {
            case 0:
                resp[2] = ("where_world", null);
                break;
            case 1:
                resp[2] = ("how_survive", null);
                break;
        }

        if (resp[lastStack].Str is not null)
            Offer(resp[lastStack]);
        for (int i = 0; i < 3; ++i)
        {
            if (i != lastStack && resp[i].Str is not null)
                Offer(resp[i]);
        }
        if (GetGameState("MAIDENS_ON_SHIP") != 0)
            Response("important_duty", GiveMaidens);

        T.ConstructResponse("bye0", "", _shofixtiName, "", "bye1");
        T.DoResponsePhrase("bye0", ExitConversation, Talk.SharedPhraseBuffer);
    }

    // DoResponsePhrase (pStr, Friendly, c_buf): its own text, or the phrase's without one.
    private void Offer((string? Str, PhraseBuffer? Buf) resp)
    {
        if (resp.Buf is { } buf)
            T.DoResponsePhrase(resp.Str!, Friendly, buf);
        else
            Response(resp.Str!, Friendly);
    }

    protected internal override void Intro()
    {
        if (T.Activity == TalkActivity.WonLastBattle)
        {
            NPCPhrase("OUT_TAKES");

            T.SetSegue(Segue.Peace);
            return;
        }

        GetShofixtiName();

        if (GetGameState("SHOFIXTI_STACK2") > 2)
        {
            NPCPhrase("FRIENDLY_HELLO");

            Friendly(0);
        }
        else
        {
            int numVisits = GetGameState("SHOFIXTI_VISITS");
            if (GetGameState("SHOFIXTI_KIA") != 0)
            {
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("HOSTILE_KATANA_1");
                        break;
                    case 1:
                        NPCPhrase("HOSTILE_KATANA_2");
                        break;
                    case 2:
                        NPCPhrase("HOSTILE_KATANA_3");
                        break;
                    case 3:
                        NPCPhrase("HOSTILE_KATANA_4");
                        --numVisits;
                        break;
                }
            }
            else
            {
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("HOSTILE_TANAKA_1");
                        break;
                    case 1:
                        NPCPhrase("HOSTILE_TANAKA_2");
                        break;
                    case 2:
                        NPCPhrase("HOSTILE_TANAKA_3");
                        break;
                    case 3:
                        NPCPhrase("HOSTILE_TANAKA_4");
                        break;
                    case 4:
                        NPCPhrase("HOSTILE_TANAKA_5");
                        break;
                    case 5:
                        NPCPhrase("HOSTILE_TANAKA_6");
                        break;
                    case 6:
                        NPCPhrase("HOSTILE_TANAKA_7");
                        break;
                    case 7:
                        NPCPhrase("HOSTILE_TANAKA_8");
                        --numVisits;
                        break;
                }
            }
            SetGameState("SHOFIXTI_VISITS", numVisits);

            Hostile(0);
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        T.SetSegue(Segue.Peace);
    }
}
