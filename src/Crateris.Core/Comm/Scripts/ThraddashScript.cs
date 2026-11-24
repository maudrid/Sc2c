// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/thradd/thraddc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Thraddash (thraddc.c), whole: in space, at their homeworld
/// (GLOBAL_FLAGS_AND_DATA bit 7) and at the Aqua Helix's world (bit 6).
/// Hostile from init until they worship the players (THRADD_MANNER), so
/// the encounter screen comes first. The hostile talk: their greetings by
/// place and visit, and their praise as the body count passes 8 and 16
/// (THRADDASH_BODY_COUNT, counted by the battles' end; THRADD_BODY_LEVEL);
/// their news (THRADD_INFO), themselves and the Ur-Quan
/// (THRADD_STACK_1), down to "I've got an idea": at peace, their mission
/// to impress the Ur-Quan queued for today (ADVANCE_THRADD_MISSION, which
/// stage 3c runs: set aside until then) unless the Kohr-Ah have won; the
/// mission's greetings (THRADD_MISSION 1 to 3, THRADD_MISSION_VISITS) and,
/// once they're back (4), how it went; every other way out of the hostile
/// talk a fight. At the Helix's world: their questions kept in the
/// culture's fields (THRADD_CULTURE, THRADD_INTRO and THRADD_DEMEANOR,
/// which the alliance's choices write over), every way out a fight. With
/// 25 of their ships destroyed (THRADDASH_BODY_THRESHOLD) the next
/// hostile greeting in space or at home is their worship: allied at once
/// (SetRaceAllied, THRADD_MANNER 1, their visits counted afresh), then the
/// players' three choices for their new culture: how they greet
/// (THRADD_INTRO), its name (THRADD_CULTURE: the captain's name spliced
/// into "the slave empire of"), and how they act (THRADD_DEMEANOR). The
/// allies: greeted in the manner chosen, the culture's name spliced into
/// their welcome by place; their news, how the culture goes (told once in
/// full, THRADD_DEMEANOR's bit 2), and at the Helix's world leave to land
/// (HELIX_UNPROTECTED). Once the Helix is taken (AQUA_HELIX) they fight
/// wherever they're met; while the Ilwrath fight them
/// (ILWRATH_FIGHT_THRADDASH) a greeting is the whole talk, at peace. The
/// endgame's OUT_TAKES. Every path is checked against UQM's own script
/// (tests/golden/talks-thradd.json).
/// <para>
/// UQM's talk can't be left before its end; the port's can (its player
/// leaves, or the host saves). The worship writes the alliance at once
/// and the three choices one by one, and a culture with no name is a
/// state UQM's script asserts against: a talk dropped before the three
/// are made settles those not yet made as the first reply of each would
/// (polite, "you decide", contemplative:
/// <see cref="DroppedUnfinished"/>), writing over what the Helix's
/// questions left in those fields as the choices would have.
/// </para>
/// </summary>
public sealed class ThraddashScript : CommScript
{
    // races.h's THRADDASH_SHIP.
    private const int ThraddashShip = 8;

    // globdata.h's THRADDASH_BODY_THRESHOLD.
    private const int BodyThreshold = 25;

    // The port's own: how many of the worship's three choices are made
    // (0: no worship in this talk; 1: begun, none made; 2: the greeting;
    // 3: the name too; 4: all three), for a talk dropped between them.
    private int _worship;

    public ThraddashScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private int Flags => GetGameState("GLOBAL_FLAGS_AND_DATA");

    // The culture's name's phrase, 0 (nothing said) for none: UQM asserts
    // there's one (thraddc.c's GetCultureName), which its own talks ensure.
    private int GetCultureName() => GetGameState("THRADD_CULTURE") switch
    {
        1 => T.Phrase("CULTURE"),
        2 => T.Phrase("FAT_JERKS"),
        3 => T.Phrase("SLAVE_EMPIRE"),
        _ => 0,
    };

    // PolitePhrase, RhymePhrase, PigLatinPhrase and LikeYouPhrase: the allies' greeting by visit.
    private void MannerPhrase(string manner, int whichPhrase)
    {
        if (whichPhrase is >= 0 and <= 3)
            NPCPhrase($"HELLO_{manner}_{whichPhrase + 1}");
    }

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Hostile);

        if (PlayerSaid(r, "bye_hostile_2"))
            NPCPhrase("GOODBYE_HOSTILE_2");
        else if (PlayerSaid(r, "bye_hostile_1"))
        {
            NPCPhrase("GOODBYE_HOSTILE_1");

            SetGameState("THRADD_HOSTILE_STACK_5", 1);
        }
        else if (PlayerSaid(r, "submit_1"))
        {
            NPCPhrase("NO_SUBMIT_1");

            SetGameState("THRADD_HOSTILE_STACK_2", 1);
        }
        else if (PlayerSaid(r, "submit_2"))
            NPCPhrase("NO_SUBMIT_2");
        else if (PlayerSaid(r, "got_idea"))
        {
            NPCPhrase("GOOD_IDEA");

            T.SetSegue(Segue.Peace);
            // Due today: stage 3c's event, set aside until then (the plan's question 3).
            T.AddEvent(relative: true, 0, 0, 0, "ADVANCE_THRADD_MISSION");
            SetGameState("THRADD_STACK_1", 5);
        }
        else if (PlayerSaid(r, "bye_hostile_helix"))
            NPCPhrase("GOODBYE_HOSTILE_HELIX");
        else if (PlayerSaid(r, "bye_ally"))
        {
            int numVisits = GetGameState("THRADD_STACK_1");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GOODBYE_ALLY_1");
                    break;
                case 1:
                    NPCPhrase("GOODBYE_ALLY_2");
                    break;
                case 2:
                    NPCPhrase("GOODBYE_ALLY_3");
                    break;
                case 3:
                    NPCPhrase("GOODBYE_ALLY_4");
                    --numVisits;
                    break;
            }
            SetGameState("THRADD_STACK_1", numVisits);
            T.SetSegue(Segue.Peace);
        }
        else if (PlayerSaid(r, "may_i_land"))
        {
            NPCPhrase("SURE_LAND");

            SetGameState("HELIX_UNPROTECTED", 1);
            T.SetSegue(Segue.Peace);
        }
        else if (PlayerSaid(r, "demand_to_land"))
            NPCPhrase("NO_DEMAND");
        else if (PlayerSaid(r, "i_need_to_land_lie"))
            NPCPhrase("CAUGHT_LIE");
        else
        {
            if (PlayerSaid(r, "contemplative"))
            {
                NPCPhrase("OK_CONTEMPLATIVE");

                SetGameState("THRADD_DEMEANOR", 0);
            }
            else if (PlayerSaid(r, "friendly"))
            {
                NPCPhrase("OK_FRIENDLY");

                SetGameState("THRADD_DEMEANOR", 1);
            }
            else if (PlayerSaid(r, "wacky"))
            {
                NPCPhrase("OK_WACKY");

                SetGameState("THRADD_DEMEANOR", 2);
            }
            else if (PlayerSaid(r, "just_like_us"))
            {
                NPCPhrase("OK_JUST_LIKE_YOU");

                SetGameState("THRADD_DEMEANOR", 3);
            }
            NPCPhrase("WORK_TO_DO");

            T.SetSegue(Segue.Peace);
            _worship = 4;
        }
    }

    private void ThraddAllies(int r)
    {
        int numVisits;

        if (PlayerSaid(r, "why_you_here_ally"))
        {
            NPCPhrase("GUARDING_HELIX_ALLY");

            DisablePhrase("why_you_here_ally");
        }
        else if (PlayerSaid(r, "whats_helix_ally"))
        {
            NPCPhrase("HELIX_IS_ALLY");

            DisablePhrase("whats_helix_ally");
        }
        else if (PlayerSaid(r, "whats_up_ally"))
        {
            numVisits = GetGameState("THRADD_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_ALLY_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_ALLY_2");
                    break;
                case 2:
                    NPCPhrase("GENERAL_INFO_ALLY_3");
                    break;
                case 3:
                    NPCPhrase("GENERAL_INFO_ALLY_4");
                    --numVisits;
                    break;
            }
            SetGameState("THRADD_INFO", numVisits);

            DisablePhrase("whats_up_ally");
        }
        else if (PlayerSaid(r, "how_goes_culture"))
        {
            // The manner in the low two bits; bit 2 once it's been told.
            numVisits = GetGameState("THRADD_DEMEANOR");
            bool first = (numVisits & ~((1 << 2) - 1)) == 0;
            switch (numVisits & ((1 << 2) - 1))
            {
                case 0:
                    NPCPhrase(first ? "CONTEMP_GOES_1" : "CONTEMP_GOES_2");
                    break;
                case 1:
                    NPCPhrase(first ? "FRIENDLY_GOES_1" : "FRIENDLY_GOES_2");
                    break;
                case 2:
                    NPCPhrase(first ? "WACKY_GOES_1" : "WACKY_GOES_2");
                    break;
                case 3:
                    NPCPhrase(first ? "LIKE_YOU_GOES_1" : "LIKE_YOU_GOES_2");
                    break;
            }
            numVisits |= 1 << 2;
            SetGameState("THRADD_DEMEANOR", numVisits);

            DisablePhrase("how_goes_culture");
        }

        if ((Flags & (1 << 6)) != 0)
        {
            if (PhraseEnabled("why_you_here_ally"))
                Response("why_you_here_ally", ThraddAllies);
            else
            {
                if (PhraseEnabled("whats_helix_ally"))
                    Response("whats_helix_ally", ThraddAllies);
                Response("may_i_land", ExitConversation);
            }
        }
        if (PhraseEnabled("whats_up_ally"))
            Response("whats_up_ally", ThraddAllies);
        if (PhraseEnabled("how_goes_culture"))
            Response("how_goes_culture", ThraddAllies);
        Response("bye_ally", ExitConversation);
    }

    private void ThraddDemeanor(int r)
    {
        if (PlayerSaid(r, "you_decide"))
        {
            NPCPhrase("OK_CULTURE_20");

            SetGameState("THRADD_CULTURE", 1);
        }
        else if (PlayerSaid(r, "fat"))
        {
            NPCPhrase("OK_FAT");

            SetGameState("THRADD_CULTURE", 2);
        }
        else if (PlayerSaid(r, "the_slave_empire0"))
        {
            SetGameState("THRADD_CULTURE", 3);

            NPCPhrase("OK_SLAVE");
        }
        _worship = 3;

        NPCPhrase("HOW_SHOULD_WE_ACT");
        Response("contemplative", ExitConversation);
        Response("friendly", ExitConversation);
        Response("wacky", ExitConversation);
        Response("just_like_us", ExitConversation);
    }

    private void ThraddCulture(int r)
    {
        if (PlayerSaid(r, "be_polite"))
        {
            NPCPhrase("OK_POLITE");

            SetGameState("THRADD_INTRO", 0);
        }
        else if (PlayerSaid(r, "use_rhymes"))
        {
            NPCPhrase("OK_RHYMES");

            SetGameState("THRADD_INTRO", 1);
        }
        else if (PlayerSaid(r, "speak_pig_latin"))
        {
            NPCPhrase("OK_PIG_LATIN");

            SetGameState("THRADD_INTRO", 2);
        }
        else if (PlayerSaid(r, "just_the_way_we_do"))
        {
            NPCPhrase("OK_WAY_YOU_DO");

            SetGameState("THRADD_INTRO", 3);
        }
        _worship = 2;
        NPCPhrase("WHAT_NAME_FOR_CULTURE");

        T.ConstructResponse("the_slave_empire0", Sis.CaptainName, "the_slave_empire1");

        Response("you_decide", ThraddDemeanor);
        Response("fat", ThraddDemeanor);
        T.DoResponsePhrase("the_slave_empire0", ThraddDemeanor, Talk.SharedPhraseBuffer);
    }

    private void ThraddWorship(int r)
    {
        SetGameState("THRADD_VISITS", 0);
        SetGameState("THRADD_MANNER", 1);
        SetGameState("THRADD_STACK_1", 0);
        T.SetRaceAllied(ThraddashShip, true);
        _worship = 1;

        Response("be_polite", ThraddCulture);
        Response("speak_pig_latin", ThraddCulture);
        Response("use_rhymes", ThraddCulture);
        Response("just_the_way_we_do", ThraddCulture);
    }

    // The worship begun and its three choices not all made: a talk dropped
    // now settles the rest (see the class).
    protected internal override bool SettlesWhenDropped => _worship is >= 1 and <= 3;

    protected internal override void DroppedUnfinished(ITalkStory story)
    {
        // Each choice not yet made, as its first reply makes it: be_polite,
        // you_decide, contemplative.
        if (_worship is < 1 or > 3)
            return;
        if (_worship < 2)
            story.Set("THRADD_INTRO", 0);
        if (_worship < 3)
            story.Set("THRADD_CULTURE", 1);
        story.Set("THRADD_DEMEANOR", 0);
    }

    private void HelixWorld(int r)
    {
        if (PlayerSaid(r, "why_you_here_hostile"))
        {
            NPCPhrase("NONE_OF_YOUR_CONCERN");

            SetGameState("THRADD_CULTURE", 1);
        }
        else if (PlayerSaid(r, "what_about_this_world"))
        {
            NPCPhrase("BLUE_HELIX");

            SetGameState("THRADD_INTRO", 1);
        }
        else if (PlayerSaid(r, "whats_helix_hostile"))
        {
            NPCPhrase("HELIX_IS_HOSTILE");

            SetGameState("THRADD_INTRO", 2);
        }
        else if (PlayerSaid(r, "i_need_to_land_lie"))
        {
            // Never reached: the reply is given ExitConversation (below).
            NPCPhrase("CAUGHT_LIE");

            SetGameState("THRADD_DEMEANOR", 1);
        }

        if (GetGameState("THRADD_CULTURE") == 0)
            Response("why_you_here_hostile", HelixWorld);
        else
            Response("demand_to_land", ExitConversation);
        switch (GetGameState("THRADD_INTRO"))
        {
            case 0:
                Response("what_about_this_world", HelixWorld);
                break;
            case 1:
                Response("whats_helix_hostile", HelixWorld);
                break;
        }
        if (GetGameState("THRADD_DEMEANOR") == 0)
            Response("i_need_to_land_lie", ExitConversation);
        Response("bye_hostile_helix", ExitConversation);
    }

    private void ThraddHostile(int r)
    {
        if (PlayerSaid(r, "whats_up_hostile_1"))
        {
            NPCPhrase("GENERAL_INFO_HOSTILE_1");

            SetGameState("THRADD_INFO", 1);
            DisablePhrase("whats_up_hostile_2");
        }
        else if (PlayerSaid(r, "whats_up_hostile_2"))
        {
            int numVisits = GetGameState("THRADD_INFO");
            switch (numVisits++)
            {
                case 1:
                    NPCPhrase("GENERAL_INFO_HOSTILE_2");
                    break;
                case 2:
                    NPCPhrase("GENERAL_INFO_HOSTILE_3");
                    break;
                case 3:
                    NPCPhrase("GENERAL_INFO_HOSTILE_4");
                    --numVisits;
                    break;
            }
            SetGameState("THRADD_INFO", numVisits);

            DisablePhrase("whats_up_hostile_2");
        }
        else if (PlayerSaid(r, "what_about_you_1"))
        {
            NPCPhrase("ABOUT_US_1");

            SetGameState("THRADD_STACK_1", 1);
        }
        else if (PlayerSaid(r, "what_about_you_2"))
        {
            NPCPhrase("ABOUT_US_2");

            SetGameState("THRADD_STACK_1", 2);
        }
        else if (PlayerSaid(r, "what_about_urquan_1"))
        {
            NPCPhrase("ABOUT_URQUAN_1");

            SetGameState("THRADD_STACK_1", 3);
        }
        else if (PlayerSaid(r, "what_about_urquan_2"))
        {
            NPCPhrase("ABOUT_URQUAN_2");

            SetGameState("THRADD_STACK_1", 4);
        }
        else if (PlayerSaid(r, "be_friends_1"))
        {
            NPCPhrase("NO_FRIENDS_1");

            SetGameState("THRADD_HOSTILE_STACK_3", 1);
        }
        else if (PlayerSaid(r, "be_friends_2"))
        {
            NPCPhrase("NO_FRIENDS_2");
            DisablePhrase("be_friends_2");
        }
        else if (PlayerSaid(r, "how_impressed_urquan_1"))
        {
            NPCPhrase("IMPRESSED_LIKE_SO_1");

            SetGameState("THRADD_HOSTILE_STACK_4", 1);
        }
        else if (PlayerSaid(r, "how_impressed_urquan_2"))
        {
            NPCPhrase("IMPRESSED_LIKE_SO_2");

            SetGameState("THRADD_MISSION", 5);
        }

        if (GetGameState("THRADD_INFO") == 0)
            Response("whats_up_hostile_1", ThraddHostile);
        else if (PhraseEnabled("whats_up_hostile_2"))
            Response("whats_up_hostile_2", ThraddHostile);
        switch (GetGameState("THRADD_STACK_1"))
        {
            case 0:
                Response("what_about_you_1", ThraddHostile);
                break;
            case 1:
                Response("what_about_you_2", ThraddHostile);
                break;
            case 2:
                Response("what_about_urquan_1", ThraddHostile);
                break;
            case 3:
                Response("what_about_urquan_2", ThraddHostile);
                break;
            case 4:
                if (GetGameState("KOHR_AH_FRENZY") == 0)
                    Response("got_idea", ExitConversation);
                else
                    SetGameState("THRADD_STACK_1", 5);
                break;
        }
        if (GetGameState("THRADD_HOSTILE_STACK_2") == 0)
            Response("submit_1", ExitConversation);
        else
            Response("submit_2", ExitConversation);
        if (GetGameState("THRADD_HOSTILE_STACK_3") == 0)
            Response("be_friends_1", ThraddHostile);
        else if (PhraseEnabled("be_friends_2"))
            Response("be_friends_2", ThraddHostile);
        if (GetGameState("THRADD_MISSION") == 4)
        {
            if (GetGameState("THRADD_HOSTILE_STACK_4") == 0)
                Response("how_impressed_urquan_1", ThraddHostile);
            else
                Response("how_impressed_urquan_2", ThraddHostile);
        }
        if (GetGameState("THRADD_HOSTILE_STACK_5") == 0)
            Response("bye_hostile_1", ExitConversation);
        else
            Response("bye_hostile_2", ExitConversation);
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
        int numVisits;

        if (T.Activity == TalkActivity.WonLastBattle)
        {
            NPCPhrase("OUT_TAKES");

            T.SetSegue(Segue.Peace);
            return;
        }

        if (GetGameState("AQUA_HELIX") != 0)
        {
            Greet("HELIX_VISITS", "DIE_THIEF_1", "DIE_THIEF_2");

            T.SetSegue(Segue.Hostile);
        }
        else if (GetGameState("ILWRATH_FIGHT_THRADDASH") != 0)
        {
            // No replies: the greeting is the whole talk.
            if (GetGameState("THRADD_MANNER") != 0)
                Greet("THRADD_VISITS", "HAVING_FUN_WITH_ILWRATH_1", "HAVING_FUN_WITH_ILWRATH_2");
            else
                Greet("THRADD_VISITS", "GO_AWAY_FIGHTING_ILWRATH_1", "GO_AWAY_FIGHTING_ILWRATH_2");

            T.SetSegue(Segue.Peace);
        }
        else if (GetGameState("THRADD_MANNER") != 0)
        {
            string pStr0, pStr1;

            numVisits = GetGameState("THRADD_VISITS");
            switch (GetGameState("THRADD_INTRO"))
            {
                case 0:
                    MannerPhrase("POLITE", numVisits);
                    break;
                case 1:
                    MannerPhrase("RHYME", numVisits);
                    break;
                case 2:
                    MannerPhrase("PIG_LATIN", numVisits);
                    break;
                case 3:
                    MannerPhrase("LIKE_YOU", numVisits);
                    break;
            }
            if (++numVisits < 4)
                SetGameState("THRADD_VISITS", numVisits);

            if ((Flags & (1 << 6)) != 0)
            {
                pStr0 = "WELCOME_HELIX0";
                pStr1 = "WELCOME_HELIX1";
            }
            else if ((Flags & (1 << 7)) != 0)
            {
                pStr0 = "WELCOME_HOMEWORLD0";
                pStr1 = "WELCOME_HOMEWORLD1";
            }
            else
            {
                pStr0 = "WELCOME_SPACE0";
                pStr1 = "WELCOME_SPACE1";
            }
            NPCPhrase(pStr0);
            NPCPhrase(GetCultureName());
            NPCPhrase(pStr1);

            ThraddAllies(0);
        }
        else if ((Flags & (1 << 6)) != 0)
        {
            Greet("HELIX_VISITS", "HOSTILE_HELIX_HELLO_1", "HOSTILE_HELIX_HELLO_2");

            HelixWorld(0);
        }
        else if (GetGameState("THRADDASH_BODY_COUNT") >= BodyThreshold)
        {
            NPCPhrase("AMAZING_PERFORMANCE");

            ThraddWorship(0);
        }
        else
        {
            numVisits = GetGameState("THRADDASH_BODY_COUNT");
            if (numVisits >= 16 && GetGameState("THRADD_BODY_LEVEL") == 1)
            {
                SetGameState("THRADD_BODY_LEVEL", 2);
                NPCPhrase("IMPRESSIVE_PERFORMANCE");
            }
            else if (numVisits >= 8 && GetGameState("THRADD_BODY_LEVEL") == 0)
            {
                SetGameState("THRADD_BODY_LEVEL", 1);
                NPCPhrase("ADEQUATE_PERFORMANCE");
            }

            if ((Flags & (1 << 7)) != 0)
            {
                Greet("THRADD_HOME_VISITS", "HOSTILE_HOMEWORLD_HELLO_1", "HOSTILE_HOMEWORLD_HELLO_2",
                    "HOSTILE_HOMEWORLD_HELLO_3", "HOSTILE_HOMEWORLD_HELLO_4");
            }
            else if ((numVisits = GetGameState("THRADD_MISSION")) == 0 || numVisits > 3)
            {
                Greet("THRADD_VISITS", "HOSTILE_SPACE_HELLO_1", "HOSTILE_SPACE_HELLO_2",
                    "HOSTILE_SPACE_HELLO_3", "HOSTILE_SPACE_HELLO_4");
            }
            else
            {
                bool first = GetGameState("THRADD_MISSION_VISITS") == 0;
                switch (numVisits)
                {
                    case 1:
                        NPCPhrase(first ? "WE_GO_TO_IMPRESS_URQUAN_1" : "WE_GO_TO_IMPRESS_URQUAN_2");
                        break;
                    case 2:
                        NPCPhrase(first ? "WE_IMPRESSING_URQUAN_1" : "WE_IMPRESSING_URQUAN_2");
                        break;
                    case 3:
                        NPCPhrase(first ? "WE_IMPRESSED_URQUAN_1" : "WE_IMPRESSED_URQUAN_2");
                        break;
                }
                SetGameState("THRADD_MISSION_VISITS", 1);
            }

            ThraddHostile(0);
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        if (GetGameState("THRADD_MANNER") != 0 || T.Activity == TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
