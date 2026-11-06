// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/comandr/comandr.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The starbase commander's talk before the alliance (comandr.c): the
/// radioactives, the Lunar base, the Ilwrath and the alliance's name.
/// Line for line as UQM's; the function names are UQM's.
/// </summary>
public sealed class CommanderScript : CommScript
{
    /// <summary>RADIOACTIVE (elemdata.h).</summary>
    private const int Radioactive = 6;

    /// <summary>FUEL_TANK_SCALE (sis.h).</summary>
    private const int FuelTankScale = 100;

    public CommanderScript(ConversationScript phrases) : base(phrases)
    {
    }

    private void ByeBye(int r)
    {
        if (PlayerSaid(r, "ok_i_will_get_radios"))
            NPCPhrase("THANKS_FOR_HELPING");
        else if (PlayerSaid(r, "well_go_get_them_now"))
            NPCPhrase("GLAD_WHEN_YOU_COME_BACK");
        else if (PlayerSaid(r, "we_will_take_care_of_base"))
        {
            NPCPhrase("GOOD_LUCK_WITH_BASE");

            SetGameState("WILL_DESTROY_BASE", 1);
        }
        else if (PlayerSaid(r, "take_care_of_base_again"))
            NPCPhrase("GOOD_LUCK_AGAIN");
        else if (PlayerSaid(r, "base_was_abandoned") || PlayerSaid(r, "i_lied_it_was_abandoned"))
        {
            NPCPhrase("IT_WAS_ABANDONED");
            NPCPhrase("HERE_COMES_ILWRATH");

            SetGameState("PROBE_ILWRATH_ENCOUNTER", 1);
        }
        else if (PlayerSaid(r, "oh_yes_big_fight"))
        {
            NPCPhrase("IM_GLAD_YOU_WON");
            NPCPhrase("HERE_COMES_ILWRATH");

            SetGameState("PROBE_ILWRATH_ENCOUNTER", 1);
        }
        else if (PlayerSaid(r, "i_cant_talk_about_it"))
        {
            NPCPhrase("IM_SURE_IT_WAS_DIFFICULT");
            NPCPhrase("HERE_COMES_ILWRATH");

            SetGameState("PROBE_ILWRATH_ENCOUNTER", 1);
        }
        else if (PlayerSaid(r, "cook_their_butts") || PlayerSaid(r, "overthrow_evil_aliens")
            || PlayerSaid(r, "annihilate_those_monsters"))
        {
            SetGameState("PROBE_ILWRATH_ENCOUNTER", 0);

            if (PlayerSaid(r, "cook_their_butts"))
                NPCPhrase("COOK_BUTTS");
            else if (PlayerSaid(r, "overthrow_evil_aliens"))
                NPCPhrase("OVERTHROW_ALIENS");
            else
                NPCPhrase("KILL_MONSTERS");

            T.ConstructResponse("name_40", Sis.CaptainName, "name_41");

            NPCPhrase("THIS_MAY_SEEM_SILLY");

            Response("name_1", ByeBye);
            Response("name_2", ByeBye);
            Response("name_3", ByeBye);
            T.DoResponsePhrase("name_40", ByeBye, Talk.SharedPhraseBuffer);

            SetGameState("STARBASE_AVAILABLE", 1);
        }
        else
        {
            if (PlayerSaid(r, "name_1"))
            {
                NPCPhrase("OK_THE_NAFS");

                SetGameState("NEW_ALLIANCE_NAME", 0);
            }
            else if (PlayerSaid(r, "name_2"))
            {
                NPCPhrase("OK_THE_CAN");

                SetGameState("NEW_ALLIANCE_NAME", 1);
            }
            else if (PlayerSaid(r, "name_3"))
            {
                NPCPhrase("OK_THE_UFW");

                SetGameState("NEW_ALLIANCE_NAME", 2);
            }
            else
            {
                NPCPhrase("OK_THE_NAME_IS_EMPIRE0");
                NPCPhrase(Talk.GlobalPlayerName);
                NPCPhrase("OK_THE_NAME_IS_EMPIRE1");

                SetGameState("NEW_ALLIANCE_NAME", 3);
            }

            NPCPhrase("STARBASE_WILL_BE_READY");
        }
    }

    // The commander's gifts, the same in three of his functions.
    private void NewLander()
    {
        NPCPhrase("HERE_IS_A_NEW_LANDER");
        ++Sis.Landers;
        T.DeltaSISGauges(4, 0, 0);

        SetGameState("LANDERS_LOST", 1);
    }

    private void AnotherLander()
    {
        NPCPhrase("HERE_IS_ANOTHER_LANDER");
        ++Sis.Landers;
        T.DeltaSISGauges(4, 0, 0);
    }

    private void GiveFuel()
    {
        NPCPhrase("GIVE_FUEL");
        T.DeltaSISGauges(0, 5 * FuelTankScale, 0);

        SetGameState("GIVEN_FUEL_BEFORE", 1);
    }

    private void GiveFuelAgain()
    {
        NPCPhrase("GIVE_FUEL_AGAIN");
        T.DeltaSISGauges(0, 5 * FuelTankScale, 0);
    }

    private bool NeedsLander => Sis.Landers == 0 && GetGameState("CHMMR_BOMB_STATE") < 2;

    private bool NeedsFuel => Sis.Fuel < 2 * FuelTankScale;

    private void NoRadioactives(int r)
    {
        if (PlayerSaid(r, "yes_this_is_supply_ship"))
        {
            NPCPhrase("ABOUT_TIME");

            if (Sis.Minerals[Radioactive] != 0)
                GiveRadios(0);
            else
            {
                Response("i_lied", NoRadioactives);
                Response("plumb_out", NoRadioactives);
            }
        }
        else
        {
            if (PlayerSaid(r, "where_can_i_get_radios"))
            {
                NPCPhrase("RADIOS_ON_MERCURY");

                DisablePhrase("where_can_i_get_radios");
            }
            else if (PlayerSaid(r, "no_but_well_help0"))
                NPCPhrase("THE_WHAT_FROM_WHERE");
            else if (PlayerSaid(r, "what_slave_planet") || PlayerSaid(r, "i_lied"))
                NPCPhrase("DONT_KNOW_WHO_YOU_ARE");
            else if (PlayerSaid(r, "plumb_out"))
                NPCPhrase("WHAT_KIND_OF_IDIOT");
            else if (PlayerSaid(r, "i_lost_my_lander"))
                NewLander();
            else if (PlayerSaid(r, "i_lost_another_lander"))
                AnotherLander();
            else if (PlayerSaid(r, "need_fuel_mercury") || PlayerSaid(r, "need_fuel_luna"))
                GiveFuel();
            else if (PlayerSaid(r, "need_fuel_again"))
                GiveFuelAgain();

            if (Sis.Minerals[Radioactive] != 0)
                GiveRadios(0);
            else
            {
                if (NeedsLander)
                {
                    if (GetGameState("LANDERS_LOST") != 0)
                        Response("i_lost_another_lander", NoRadioactives);
                    else
                        Response("i_lost_my_lander", NoRadioactives);
                }
                if (NeedsFuel)
                {
                    if (GetGameState("GIVEN_FUEL_BEFORE") != 0)
                        Response("need_fuel_again", NoRadioactives);
                    else
                        Response("need_fuel_mercury", NoRadioactives);
                }

                Response("ok_i_will_get_radios", ByeBye);
                if (PhraseEnabled("where_can_i_get_radios"))
                    Response("where_can_i_get_radios", NoRadioactives);
            }
        }
    }

    private void AskAfterRadios(int r)
    {
        if (PlayerSaid(r, "i_lost_my_lander"))
            NewLander();
        else if (PlayerSaid(r, "i_lost_another_lander"))
            AnotherLander();
        else if (PlayerSaid(r, "need_fuel_mercury") || PlayerSaid(r, "need_fuel_luna"))
            GiveFuel();
        else if (PlayerSaid(r, "need_fuel_again"))
            GiveFuelAgain();
        else if (PlayerSaid(r, "where_get_radios"))
        {
            NPCPhrase("RADIOS_ON_MERCURY");

            DisablePhrase("where_get_radios");
        }

        if (NeedsLander)
        {
            if (GetGameState("LANDERS_LOST") != 0)
                Response("i_lost_another_lander", AskAfterRadios);
            else
                Response("i_lost_my_lander", AskAfterRadios);
        }
        if (NeedsFuel)
        {
            if (GetGameState("GIVEN_FUEL_BEFORE") != 0)
                Response("need_fuel_again", AskAfterRadios);
            else
                Response("need_fuel_mercury", AskAfterRadios);
        }
        Response("well_go_get_them_now", ByeBye);
        if (PhraseEnabled("where_get_radios"))
            Response("where_get_radios", AskAfterRadios);
    }

    private void BaseDestroyed(int r)
    {
        if (PlayerSaid(r, "we_fought_them"))
        {
            NPCPhrase("YOU_REALLY_FOUGHT_BASE");

            Response("oh_yes_big_fight", ByeBye);
            Response("i_lied_it_was_abandoned", ByeBye);
            Response("i_cant_talk_about_it", ByeBye);
        }
        else
        {
            if (PlayerSaid(r, "we_are_here_to_help"))
                NPCPhrase("BASE_ON_MOON");
            else
                NPCPhrase("DEALT_WITH_BASE_YET");

            Response("base_was_abandoned", ByeBye);
            Response("we_fought_them", BaseDestroyed);
        }
    }

    private void TellMoonBase(int r)
    {
        if (r == 0)
            NPCPhrase("DEALT_WITH_BASE_YET");
        else if (PlayerSaid(r, "i_lost_my_lander"))
            NewLander();
        else if (PlayerSaid(r, "i_lost_another_lander"))
            AnotherLander();
        else if (PlayerSaid(r, "need_fuel_mercury") || PlayerSaid(r, "need_fuel_luna"))
            GiveFuel();
        else if (PlayerSaid(r, "need_fuel_again"))
            GiveFuelAgain();
        else if (PlayerSaid(r, "we_are_here_to_help"))
            NPCPhrase("BASE_ON_MOON");
        else if (GetGameState("STARBASE_YACK_STACK1") == 0)
        {
            NPCPhrase("ABOUT_BASE");

            SetGameState("STARBASE_YACK_STACK1", 1);
        }
        else
            NPCPhrase("ABOUT_BASE_AGAIN");

        if (NeedsLander)
        {
            if (GetGameState("LANDERS_LOST") != 0)
                Response("i_lost_another_lander", TellMoonBase);
            else
                Response("i_lost_my_lander", TellMoonBase);
        }
        if (NeedsFuel)
        {
            if (GetGameState("GIVEN_FUEL_BEFORE") != 0)
                Response("need_fuel_again", TellMoonBase);
            else
                Response("need_fuel_luna", TellMoonBase);
        }
        if (GetGameState("WILL_DESTROY_BASE") == 0)
            Response("we_will_take_care_of_base", ByeBye);
        else
            Response("take_care_of_base_again", ByeBye);
        if (GetGameState("STARBASE_YACK_STACK1") == 0)
            Response("tell_me_about_base", TellMoonBase);
        else
            Response("tell_me_again", TellMoonBase);
    }

    private void TellProbe(int r)
    {
        NPCPhrase("THAT_WAS_PROBE");
        DisablePhrase("what_was_red_thing");

        Response("it_went_away", RevealSelf);
        Response("we_destroyed_it", RevealSelf);
        Response("what_probe", RevealSelf);
    }

    private void RevealSelf(int r)
    {
        int stack = 0;
        if (PlayerSaid(r, "we_are_vindicator0"))
        {
            NPCPhrase("THATS_IMPOSSIBLE");

            DisablePhrase("we_are_vindicator0");
        }
        else if (PlayerSaid(r, "our_mission_was_secret"))
        {
            NPCPhrase("ACKNOWLEDGE_SECRET");

            DisablePhrase("our_mission_was_secret");
        }
        else if (PlayerSaid(r, "first_give_info"))
        {
            NPCPhrase("ASK_AWAY");

            stack = 1;
            DisablePhrase("first_give_info");
        }
        else if (PlayerSaid(r, "whats_this_starbase"))
        {
            NPCPhrase("STARBASE_IS");

            stack = 1;
            DisablePhrase("whats_this_starbase");
        }
        else if (PlayerSaid(r, "what_about_earth"))
        {
            NPCPhrase("HAPPENED_TO_EARTH");

            stack = 1;
            DisablePhrase("what_about_earth");
        }
        else if (PlayerSaid(r, "where_are_urquan"))
        {
            NPCPhrase("URQUAN_LEFT");

            stack = 1;
            DisablePhrase("where_are_urquan");
        }
        else if (PlayerSaid(r, "it_went_away"))
            NPCPhrase("DEEP_TROUBLE");
        else if (PlayerSaid(r, "we_destroyed_it"))
            NPCPhrase("GOOD_NEWS");
        else if (PlayerSaid(r, "what_probe"))
            NPCPhrase("SURE_HOPE");

        for (int i = 0; i < 2; ++i, stack ^= 1)
        {
            if (stack == 1)
            {
                if (PhraseEnabled("first_give_info"))
                    Response("first_give_info", RevealSelf);
                else if (PhraseEnabled("whats_this_starbase"))
                    Response("whats_this_starbase", RevealSelf);
                else if (PhraseEnabled("what_about_earth"))
                    Response("what_about_earth", RevealSelf);
                else if (PhraseEnabled("where_are_urquan"))
                    Response("where_are_urquan", RevealSelf);
                else if (PhraseEnabled("what_was_red_thing"))
                    Response("what_was_red_thing", TellProbe);
            }
            else
            {
                if (PhraseEnabled("we_are_vindicator0"))
                {
                    T.ConstructResponse("we_are_vindicator0", Sis.CaptainName, "we_are_vindicator1",
                        Sis.ShipName, "we_are_vindicator2");
                    T.DoResponsePhrase("we_are_vindicator0", RevealSelf, Talk.SharedPhraseBuffer);
                }
                else if (PhraseEnabled("our_mission_was_secret"))
                    Response("our_mission_was_secret", RevealSelf);
                else
                {
                    if (GetGameState("MOONBASE_DESTROYED") == 0)
                        Response("we_are_here_to_help", TellMoonBase);
                    else
                        Response("we_are_here_to_help", BaseDestroyed);
                }
            }
        }
    }

    private void GiveRadios(int r)
    {
        if (PlayerSaid(r, "we_will_transfer_now"))
        {
            SetGameState("RADIOACTIVES_PROVIDED", 1);

            NPCPhrase("FUEL_UP0");
            NPCPhrase("FUEL_UP1");
            T.AlienTalkSegue(1);

            T.Ambient[2] |= CommAnimationFlags.Disabled;

            T.XFormColorMap(0, Talk.OneSecond / 2);

            T.AlienTalkSegue(Talk.WaitAll);

            RevealSelf(0);
        }
        else
        {
            if (PlayerSaid(r, "what_will_you_give_us"))
                NPCPhrase("MESSAGE_GARBLED_1");
            else if (PlayerSaid(r, "before_radios_we_need_info"))
                NPCPhrase("MESSAGE_GARBLED_2");

            Response("we_will_transfer_now", GiveRadios);
            Response("what_will_you_give_us", GiveRadios);
            Response("before_radios_we_need_info", GiveRadios);
        }
    }

    protected internal override void Intro()
    {
        if (GetGameState("PROBE_ILWRATH_ENCOUNTER") != 0)
        {
            NPCPhrase("VERY_IMPRESSIVE");

            Response("cook_their_butts", ByeBye);
            Response("overthrow_evil_aliens", ByeBye);
            Response("annihilate_those_monsters", ByeBye);
        }
        else if (GetGameState("STARBASE_VISITED") != 0)
        {
            if (GetGameState("RADIOACTIVES_PROVIDED") != 0)
            {
                if (GetGameState("MOONBASE_DESTROYED") == 0)
                    TellMoonBase(0);
                else
                    BaseDestroyed(0);
            }
            else
            {
                T.SetColorMap(1);
                NPCPhrase("DO_YOU_HAVE_RADIO_THIS_TIME");

                if (Sis.Minerals[Radioactive] != 0)
                    GiveRadios(0);
                else
                    AskAfterRadios(0);
            }
        }
        else
        {
            // The first visit.
            T.SetColorMap(1);

            SetGameState("STARBASE_VISITED", 1);

            NPCPhrase("ARE_YOU_SUPPLY_SHIP");
            T.ConstructResponse("no_but_well_help0", Sis.ShipName, "no_but_well_help1");
            T.DoResponsePhrase("no_but_well_help0", NoRadioactives, Talk.SharedPhraseBuffer);
            Response("yes_this_is_supply_ship", NoRadioactives);
            Response("what_slave_planet", NoRadioactives);
        }
    }

    protected internal override void Init()
    {
        if (GetGameState("RADIOACTIVES_PROVIDED") != 0)
        {
            T.Ambient[2] |= CommAnimationFlags.Disabled;
            // The regular track.
            T.UseAltSong = false;
        }
        else
        {
            T.Ambient[2] &= ~CommAnimationFlags.Disabled;
            // The low-power track, if there is one.
            T.AltSong = "comm.commander.lowpower.music";
            T.UseAltSong = true;
        }

        // AlienTextWidth and AlienTextBaseline: in the description already.
        T.SetSegue(Segue.Peace);
    }
}
