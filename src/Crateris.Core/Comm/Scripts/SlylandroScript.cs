// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/slyland/slyland.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;
using Crateris.Core.Uqm;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Slylandro probe's talk (slyland.c), whole: a probe met in hyperspace
/// or a system greets the ship (by its visits), offers its five replies,
/// each answered by its own count of times asked, and every one ends in
/// battle (PROBE_EXHIBITED_BUG); with the destruct code aboard
/// (DESTRUCT_CODE_ON_SHIP, the Slylandro at home's, 3b-2's) the code sent
/// makes it destroy itself: an instant victory. Asked who it is a fourth
/// time, it says where it is, in its own coordinates (relative to B Corvi),
/// spoken with its number speech. Every path is checked against UQM's own
/// script (tests/golden/talks.json).
/// </summary>
public sealed class SlylandroScript : CommScript
{
    public SlylandroScript(ConversationScript phrases) : base(phrases)
    {
    }

    // probe_numbers_english: 1000s (spoken as a number, then "thousand"),
    // 100s ("hundred"), the tens, the teens, the digits.
    private static readonly string?[] DigitNames =
    {
        "ENUMERATE_ZERO", "ENUMERATE_ONE", "ENUMERATE_TWO", "ENUMERATE_THREE", "ENUMERATE_FOUR",
        "ENUMERATE_FIVE", "ENUMERATE_SIX", "ENUMERATE_SEVEN", "ENUMERATE_EIGHT", "ENUMERATE_NINE",
    };

    private static readonly string?[] TeenNames =
    {
        "ENUMERATE_TEN", "ENUMERATE_ELEVEN", "ENUMERATE_TWELVE", "ENUMERATE_THIRTEEN", "ENUMERATE_FOURTEEN",
        "ENUMERATE_FIFTEEN", "ENUMERATE_SIXTEEN", "ENUMERATE_SEVENTEEN", "ENUMERATE_EIGHTEEN", "ENUMERATE_NINETEEN",
    };

    private static readonly string?[] TensNames =
    {
        null, null, "ENUMERATE_TWENTY", "ENUMERATE_THIRTY", "ENUMERATE_FOURTY",
        "ENUMERATE_FIFTY", "ENUMERATE_SIXTY", "ENUMERATE_SEVENTY", "ENUMERATE_EIGHTY", "ENUMERATE_NINETY",
    };

    private static readonly NumberSpeech ProbeNumbersEnglish = new(new SpeechDigit[]
    {
        new(1000, 0, null, null, "ENUMERATE_THOUSAND"),
        new(100, 0, DigitNames, null, "ENUMERATE_HUNDRED"),
        new(10, 0, TensNames, null, null),
        new(1, 10, TeenNames, null, null),
        new(1, 0, DigitNames, null, null),
    });

    protected internal override NumberSpeech NumberSpeech => ProbeNumbersEnglish;

    // The replies offered (static RESPONSE_REFs, kept between the replies).
    private string _threat = "", _somethingWrong = "", _weAreUs = "", _whyAttack = "", _bye = "";

    private void SayCoord(int coord)
    {
        int ac = Math.Abs(coord);

        T.NPCPhraseSplice(coord < 0 ? "COORD_MINUS" : "COORD_PLUS");
        T.NPCNumber(ac / 10, "%03d");
        T.NPCPhraseSplice("COORD_POINT");
        T.NPCNumber(ac % 10);
    }

    private static string Pick(int count, string reply) => reply + "_" + Math.Min(count + 1, 4);

    private void CombatIsInevitable(int r)
    {
        if (r == 0)
        {
            if (GetGameState("DESTRUCT_CODE_ON_SHIP") != 0)
                Response("destruct_code", CombatIsInevitable);
            _threat = Pick(GetGameState("SLYLANDRO_PROBE_THREAT"), "threat");
            Response(_threat, CombatIsInevitable);
            _somethingWrong = Pick(GetGameState("SLYLANDRO_PROBE_WRONG"), "something_wrong");
            Response(_somethingWrong, CombatIsInevitable);
            _weAreUs = Pick(GetGameState("SLYLANDRO_PROBE_ID"), "we_are_us");
            Response(_weAreUs, CombatIsInevitable);
            _whyAttack = Pick(GetGameState("SLYLANDRO_PROBE_INFO"), "why_attack");
            Response(_whyAttack, CombatIsInevitable);
            _bye = Pick(GetGameState("SLYLANDRO_PROBE_EXIT"), "bye");
            Response(_bye, CombatIsInevitable);
        }
        else if (PlayerSaid(r, "destruct_code"))
        {
            NPCPhrase("DESTRUCT_SEQUENCE");
            T.SetSegue(Segue.Victory);
        }
        else
        {
            if (PlayerSaid(r, _threat))
                Answer("SLYLANDRO_PROBE_THREAT", "PROGRAMMED_TO_DEFEND");
            else if (PlayerSaid(r, _somethingWrong))
                Answer("SLYLANDRO_PROBE_WRONG", "NOMINAL_FUNCTION");
            else if (PlayerSaid(r, _weAreUs))
            {
                int numVisits = GetGameState("SLYLANDRO_PROBE_ID");
                switch (numVisits++)
                {
                    case 0:
                        NPCPhrase("THIS_IS_PROBE_1");
                        break;
                    case 1:
                        NPCPhrase("THIS_IS_PROBE_2");
                        break;
                    case 2:
                        NPCPhrase("THIS_IS_PROBE_3");
                        break;
                    case 3:
                    {
                        // Probe's coordinate system is {-Y,X} relative to B Corvi
                        var (logX, logY) = Sis.LogPlace;
                        int dx = Units.LogXToUniverse(logX) - 333;
                        int dy = 9812 - Units.LogYToUniverse(logY);

                        NPCPhrase("THIS_IS_PROBE_40");
                        SayCoord(dy);
                        T.NPCPhraseSplice("THIS_IS_PROBE_41");
                        SayCoord(dx);
                        NPCPhrase("THIS_IS_PROBE_42");

                        --numVisits;
                        break;
                    }
                }
                SetGameState("SLYLANDRO_PROBE_ID", numVisits);
            }
            else if (PlayerSaid(r, _whyAttack))
                Answer("SLYLANDRO_PROBE_INFO", "PEACEFUL_MISSION");
            else if (PlayerSaid(r, _bye))
                Answer("SLYLANDRO_PROBE_EXIT", "GOODBYE");

            NPCPhrase("HOSTILE");

            SetGameState("PROBE_EXHIBITED_BUG", 1);
            T.SetSegue(Segue.Hostile);
        }
    }

    // A reply's answer by how often it was asked before (the fourth and
    // after the same), the count kept.
    private void Answer(string counter, string answer)
    {
        int numVisits = GetGameState(counter);
        switch (numVisits++)
        {
            case 0:
                NPCPhrase(answer + "_1");
                break;
            case 1:
                NPCPhrase(answer + "_2");
                break;
            case 2:
                NPCPhrase(answer + "_3");
                break;
            case 3:
                NPCPhrase(answer + "_4");
                --numVisits;
                break;
        }
        SetGameState(counter, numVisits);
    }

    protected internal override void Intro()
    {
        int numVisits = GetGameState("SLYLANDRO_PROBE_VISITS");
        switch (numVisits++)
        {
            case 0:
                NPCPhrase("WE_COME_IN_PEACE_1");
                break;
            case 1:
                NPCPhrase("WE_COME_IN_PEACE_2");
                break;
            case 2:
                NPCPhrase("WE_COME_IN_PEACE_3");
                break;
            case 3:
                NPCPhrase("WE_COME_IN_PEACE_4");
                break;
            case 4:
                NPCPhrase("WE_COME_IN_PEACE_5");
                break;
            case 5:
                NPCPhrase("WE_COME_IN_PEACE_6");
                break;
            case 6:
                NPCPhrase("WE_COME_IN_PEACE_7");
                break;
            case 7:
                NPCPhrase("WE_COME_IN_PEACE_8");
                --numVisits;
                break;
        }
        SetGameState("SLYLANDRO_PROBE_VISITS", numVisits);

        CombatIsInevitable(0);
    }

    // uninit_slyland returns 0; post_slyland_enc: nothing defined so far.

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        T.SetSegue(Segue.Hostile);
    }
}
