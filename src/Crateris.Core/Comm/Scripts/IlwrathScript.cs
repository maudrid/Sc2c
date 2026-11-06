// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/ilwrath/ilwrathc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Ilwrath's talk (ilwrathc.c), as far as stage 3a needs it: the ship
/// that answers the Ur-Quan probe's call at Earth (PROBE_ILWRATH_ENCOUNTER)
/// and its four replies, which all end in battle. The Ilwrath met in
/// space, at their homeworld or by broadcaster come with stage 3b.
/// </summary>
public sealed class IlwrathScript : CommScript
{
    public IlwrathScript(ConversationScript phrases) : base(phrases)
    {
    }

    private void CombatIsInevitable(int r)
    {
        T.SetSegue(Segue.Hostile);

        if (PlayerSaid(r, "you_are_weak"))
            NPCPhrase("STRENGTH_NOT_ALL");
        else if (PlayerSaid(r, "slay_by_thousands"))
            NPCPhrase("NO_SLAY_BY_THOUSANDS");
        else if (PlayerSaid(r, "ease_up"))
            NPCPhrase("NO_EASE_UP");
        else if (PlayerSaid(r, "bye_space"))
            NPCPhrase("GOODBYE_AND_DIE_SPACE");
        else if (PlayerSaid(r, "bye_homeworld"))
            NPCPhrase("GOODBYE_AND_DIE_HOMEWORLD");
        else if (PlayerSaid(r, "want_peace"))
            NPCPhrase("NO_PEACE");
        else if (PlayerSaid(r, "want_alliance"))
            NPCPhrase("NO_ALLIANCE");
        else if (PlayerSaid(r, "but_evil_is_defined"))
            NPCPhrase("DONT_CONFUSE_US");
        else if (PlayerSaid(r, "bye_gods"))
        {
            NPCPhrase("GOODBYE_GODS");

            T.SetSegue(Segue.Peace);
        }
        if (PlayerSaid(r, "whats_up"))
        {
            NPCPhrase("GENERAL_INFO");
            Response("bye", CombatIsInevitable);
        }
        else if (PlayerSaid(r, "whats_up_space_1") || PlayerSaid(r, "whats_up_space_2")
            || PlayerSaid(r, "whats_up_space_3") || PlayerSaid(r, "whats_up_space_4")
            || PlayerSaid(r, "whats_up_space_5"))
        {
            int numVisits = GetGameState("ILWRATH_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_SPACE_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_SPACE_2");
                    break;
                case 2:
                    NPCPhrase("GENERAL_INFO_SPACE_3");
                    break;
                case 3:
                    NPCPhrase("GENERAL_INFO_SPACE_4");
                    break;
                case 4:
                    NPCPhrase("GENERAL_INFO_SPACE_5");
                    --numVisits;
                    break;
            }
            SetGameState("ILWRATH_INFO", numVisits);
        }
        else
        {
            if (PlayerSaid(r, "bye"))
                NPCPhrase("GOODBYE_AND_DIE");
            else if (PlayerSaid(r, "where_you_come_from"))
                NPCPhrase("CAME_FROM");
            if (PlayerSaid(r, "it_will_be_a_pleasure"))
                NPCPhrase("WHO_BLASTS_WHO");
            if (PlayerSaid(r, "surrender"))
                NPCPhrase("NO_SURRENDER");
            if (PlayerSaid(r, "be_reasonable"))
                NPCPhrase("NOT_REASONABLE");
        }
    }

    protected internal override void Intro()
    {
        if (GetGameState("PROBE_ILWRATH_ENCOUNTER") == 0)
            throw new NotSupportedException("the Ilwrath met anywhere but at Earth come with stage 3b");

        NPCPhrase("SEND_MESSAGE");

        Response("where_you_come_from", CombatIsInevitable);
        Response("it_will_be_a_pleasure", CombatIsInevitable);
        Response("surrender", CombatIsInevitable);
        Response("be_reasonable", CombatIsInevitable);
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        // (WON_LAST_BATTLE, the other way to peace, is stage 4's.)
        if (GetGameState("PROBE_ILWRATH_ENCOUNTER") != 0
            || (GetGameState("GLOBAL_FLAGS_AND_DATA") & ((1 << 4) | (1 << 5))) != 0)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
