// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/rebel/rebel.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Yehat rebels (rebel.c), whole: always at peace, with their own
/// song (REBEL_MUSIC). Their greetings (YEHAT_REBEL_VISITS), the revolution's
/// news (YEHAT_REBEL_INFO), the Pkunk, their information (asked until none
/// is left), and their ships: once a month, as many as the escort fleet
/// (the host's) has room for up to 4, royalist Yehat ships (YEHAT_SHIP, not
/// their own), the next ships' date kept (YEHAT_SHIP_MONTH, _DAY, _YEAR).
/// In the endgame's last battle (IN_LAST_BATTLE), the cavalry: up to 8
/// ships, half rebel Yehat and half Pkunk. Met only once the revolt's event
/// has given them a fleet (stage 3c), so until then a group of them is
/// brought by --start-encounter YEHAT_REBEL_SHIP, and --start-state sets the
/// story their paths read. Every path is checked against UQM's own script
/// (tests/golden/talks-rebel.json).
/// </summary>
public sealed class YehatRebelScript : CommScript
{
    // races.h's PKUNK_SHIP, YEHAT_SHIP and YEHAT_REBEL_SHIP.
    private const int PkunkShip = 4, YehatShip = 11, YehatRebelShip = 22;

    // NUM_YEHAT_SHIPS: the most they give at a time.
    private const int NumYehatShips = 4;

    // START_YEAR (globdata.h).
    private const int StartYear = 2155;

    public YehatRebelScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    // PrepareShip: the next ships' date, a month on (its day at most the 28th).
    private void PrepareShip()
    {
        int mi = (T.Clock.Month % 12) + 1;
        SetGameState("YEHAT_SHIP_MONTH", mi);
        int di = T.Clock.Day;
        if (di > 28)
            di = 28;
        SetGameState("YEHAT_SHIP_DAY", di);
        int yi = (byte)(T.Clock.Year - StartYear);
        if (mi == 1)
            ++yi;
        SetGameState("YEHAT_SHIP_YEAR", (byte)yi);
    }

    private void ExitConversation(int r)
    {
        if (PlayerSaid(r, "bye_rebel"))
            NPCPhrase("GOODBYE_REBEL");
    }

    private void RebelInfo(int r)
    {
        bool infoLeft = false;
        if (PlayerSaid(r, "give_info_rebels"))
            NPCPhrase("WHAT_INFO");
        else if (PlayerSaid(r, "what_about_urquan"))
        {
            NPCPhrase("ABOUT_URQUAN");

            DisablePhrase("what_about_urquan");
        }
        else if (PlayerSaid(r, "what_about_royalty"))
        {
            NPCPhrase("ABOUT_ROYALTY");

            DisablePhrase("what_about_royalty");
        }
        else if (PlayerSaid(r, "what_about_war"))
        {
            NPCPhrase("ABOUT_WAR");

            DisablePhrase("what_about_war");
        }
        else if (PlayerSaid(r, "what_about_vux"))
        {
            NPCPhrase("ABOUT_VUX");

            DisablePhrase("what_about_vux");
        }
        else if (PlayerSaid(r, "what_about_clue"))
        {
            NPCPhrase("ABOUT_CLUE");

            DisablePhrase("what_about_clue");
        }

        foreach (var phrase in new[] { "what_about_urquan", "what_about_royalty", "what_about_war", "what_about_vux", "what_about_clue" })
        {
            if (PhraseEnabled(phrase))
            {
                Response(phrase, RebelInfo);
                infoLeft = true;
            }
        }
        Response("enough_info", Rebels);

        if (!infoLeft)
            DisablePhrase("give_info_rebels");
    }

    private void Rebels(int r)
    {
        sbyte numVisits;

        if (PlayerSaid(r, "how_goes_revolution"))
        {
            numVisits = (sbyte)GetGameState("YEHAT_REBEL_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("REBEL_REVOLUTION_1");
                    break;
                case 1:
                    NPCPhrase("REBEL_REVOLUTION_2");
                    break;
                case 2:
                    NPCPhrase("REBEL_REVOLUTION_3");
                    break;
                case 3:
                    NPCPhrase("REBEL_REVOLUTION_4");
                    --numVisits;
                    break;
            }
            SetGameState("YEHAT_REBEL_INFO", numVisits);

            DisablePhrase("how_goes_revolution");
        }
        else if (PlayerSaid(r, "any_ships"))
        {
            // rebel.c's test, its SBYTE assignments as C makes them: not
            // before the next ships' date (year, then month, then day).
            if (GetGameState("YEHAT_SHIP_MONTH") != 0
                && ((numVisits = (sbyte)((T.Clock.Year - StartYear) - GetGameState("YEHAT_SHIP_YEAR"))) < 0
                    || ((numVisits == 0 && (numVisits = (sbyte)(T.Clock.Month - GetGameState("YEHAT_SHIP_MONTH"))) < 0)
                        || (numVisits == 0 && T.Clock.Day < GetGameState("YEHAT_SHIP_DAY")))))
                NPCPhrase("NO_SHIPS_YET");
            else if ((numVisits = (sbyte)Sis.EscortRoom) == 0)   // EscortFeasibilityStudy (YEHAT_SHIP)
                NPCPhrase("NO_ROOM");
            else
            {
                if (numVisits < NumYehatShips)
                    NPCPhrase("HAVE_FEW_SHIPS");
                else
                {
                    numVisits = NumYehatShips;
                    NPCPhrase("HAVE_ALL_SHIPS");
                }

                T.AlienTalkSegue(Talk.WaitAll);
                T.AddEscortShips(YehatShip, numVisits);
                PrepareShip();
            }

            DisablePhrase("any_ships");
        }
        else if (PlayerSaid(r, "what_about_pkunk_rebel"))
        {
            if (GetGameState("YEHAT_ABSORBED_PKUNK") != 0)
                NPCPhrase("PKUNK_ABSORBED_REBEL");
            else
                NPCPhrase("HATE_PKUNK_REBEL");

            SetGameState("YEHAT_REBEL_TOLD_PKUNK", 1);
        }
        else if (PlayerSaid(r, "enough_info"))
            NPCPhrase("OK_ENOUGH_INFO");

        if (PhraseEnabled("how_goes_revolution"))
            Response("how_goes_revolution", Rebels);
        if (GetGameState("YEHAT_REBEL_TOLD_PKUNK") == 0 && GetGameState("PKUNK_VISITS") != 0
            && GetGameState("PKUNK_HOME_VISITS") != 0)
            Response("what_about_pkunk_rebel", Rebels);
        if (PhraseEnabled("any_ships"))
            Response("any_ships", Rebels);
        if (PhraseEnabled("give_info_rebels"))
            Response("give_info_rebels", RebelInfo);
        Response("bye_rebel", ExitConversation);
    }

    protected internal override void Intro()
    {
        T.SetSegue(Segue.Peace);
        if (T.Activity == TalkActivity.InLastBattle)
        {
            // The endgame's cavalry: up to 8 ships, half (rounded up) rebel Yehat, half Pkunk.
            NPCPhrase("YEHAT_CAVALRY");
            T.AlienTalkSegue(Talk.WaitAll);

            int numVisits = (byte)Sis.EscortRoom;   // EscortFeasibilityStudy (YEHAT_REBEL_SHIP)
            if (numVisits > 8)
                numVisits = 8;
            T.AddEscortShips(YehatRebelShip, numVisits - (numVisits >> 1));
            T.AddEscortShips(PkunkShip, numVisits >> 1);
        }
        else
        {
            int numVisits = GetGameState("YEHAT_REBEL_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("REBEL_HELLO_1");
                    break;
                case 1:
                    NPCPhrase("REBEL_HELLO_2");
                    break;
                case 2:
                    NPCPhrase("REBEL_HELLO_3");
                    break;
                case 3:
                    NPCPhrase("REBEL_HELLO_4");
                    --numVisits;
                    break;
            }
            SetGameState("YEHAT_REBEL_VISITS", numVisits);

            Rebels(0);
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        // The rebels' own song, if it loads (REBEL_MUSIC, LDASF_USE_ALTERNATE).
        T.AltSong = "comm.yehat.rebel.music";
        T.UseAltSong = true;
        T.SetSegue(Segue.Peace);
    }
}
