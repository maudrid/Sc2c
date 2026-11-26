// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/chmmr/chmmrc.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.World;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Chmmr (chmmrc.c), whole, at peace from init: no encounter screen
/// and no fight. Every way to them is stage 3c's (the broadcaster's talk
/// from orbit, the Sun Device that frees them, their world once
/// unleashed: the stage 3b-2 plan's question 8), so before 3c only a
/// group --start-encounter brings meets them, with --start-state for the
/// story; after its talk the group is emptied, with no battle (comm.c's
/// ReinitQueue for CHMMR_SHIP: GameWorld). Shielded (CHMMR_EMERGING
/// not set): the shielded colour map, their greetings by visit
/// (CHMMR_HOME_VISITS), their stack (CHMMR_STACK), what's going on and
/// their advice (the Ur-Quan's defeat, CHMMR_BOMB_STATE 1; the Pet, the
/// bomb, the Sun Device and the Sa-Matra by what's aboard and known).
/// Emerging: the screen faded in, their freedom, the captain's
/// introductions (the captain's and the ship's names spliced), the
/// logs, the Sa-Matra (AWARE_OF_SAMATRA), what's still needed (the Pet,
/// the bomb) and CHMMR_UNLEASHED; unleashed, not ready (their alliance
/// asked for, SetRaceAllied) or ready to improve the bomb, its
/// questions, and "proceed": the alliance, their counters, the bomb's
/// state (CHMMR_BOMB_STATE 2, UTWIG_BOMB_ON_SHIP 0) and the answering
/// ship rebuilt as the bomb ship as chmmrc.c rebuilds the flagship
/// (1,000,000 resource units, no landers, the landers' and with the
/// starbase the storage bays' prices 0, for everyone as prices are
/// shared; with the starbase no fuel but the reserve, no crew and no
/// minerals and the modules past the bomb's cleared, else the modules
/// moved up to the back; thrusters and jets in every slot and the bomb's
/// ten modules). The move to Earth (or the starbase) and the 14 days
/// (genchmmr.c's END_INTERPLANETARY), and whose ship carries the bomb,
/// are stage 3c's (question 8). After the bomb: their greetings, news
/// and advice. Every path is checked against UQM's own script
/// (tests/golden/talks-chmmr.json).
/// </summary>
public sealed class ChmmrScript : CommScript
{
    // races.h's CHMMR_SHIP.
    private const int ChmmrShip = 1;

    // sis.h's FUEL_RESERVE (FUEL_VOLUME_PER_ROW: HEFUEL_TANK_CAPACITY / MAX_FUEL_BARS).
    private const uint FuelReserve = 100 * 100 / 10;

    public ChmmrScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private void ExitConversation(int r)
    {
        T.SetSegue(Segue.Peace);

        if (PlayerSaid(r, "bye"))
            NPCPhrase("GOODBYE");
        else if (PlayerSaid(r, "bye_shielded"))
            NPCPhrase("GOODBYE_SHIELDED");
        else if (PlayerSaid(r, "bye_after_bomb"))
            NPCPhrase("GOODBYE_AFTER_BOMB");
        else if (PlayerSaid(r, "proceed"))
        {
            NPCPhrase("TAKE_2_WEEKS");

            T.SetRaceAllied(ChmmrShip, true);

            SetGameState("CHMMR_HOME_VISITS", 0);
            SetGameState("CHMMR_STACK", 0);
            SetGameState("CHMMR_BOMB_STATE", 2);
            SetGameState("UTWIG_BOMB_ON_SHIP", 0);
            Sis.ResourceUnits = 1000000;
            Sis.Landers = 0;
            T.SetModuleCost(ShipModules.PlanetLander, 0);

            // (The flagship moved to Earth's system, by Earth or the
            // starbase: log_x, log_y, ShipFacing, ip_planet, in_orbit,
            // ip_location, ShipStamp. Stage 3c's, with the bomb ship.)
            var modules = Sis.OwnModuleSlots;
            if (GetGameState("STARBASE_AVAILABLE") != 0)
            {
                // Normal game mode: transported to the starbase.
                Sis.Fuel = FuelReserve;
                Sis.Crew = 0;
                T.SetModuleCost(ShipModules.StorageBay, 0);   // disable Storage Bay
                for (int i = 0; i < Sis.Minerals.Count; ++i)
                    Sis.Minerals[i] = 0;
                for (int i = ShipModules.BombModules; i < modules.Count; ++i)
                    modules[i] = ShipModules.EmptyModule;
            }
            else
            {
                // 'Beating Game Differently' mode, never at the starbase:
                // transported to Earth, the layout compressed to the back.
                for (int i = modules.Count - 1; i > 0; --i)
                {
                    // The next unused slot.
                    for (; i > 0 && modules[i] != ShipModules.EmptyModule; --i)
                    {
                    }
                    if (i == 0)
                        break;
                    // The next module to move.
                    int m;
                    for (m = i - 1; m >= 0 && modules[m] == ShipModules.EmptyModule; --m)
                    {
                    }
                    if (m < 0)
                        break;

                    modules[i] = modules[m];
                    modules[m] = ShipModules.EmptyModule;
                }
            }

            // The Chmmr's own modules.
            var drives = Sis.OwnDriveSlots;
            for (int i = 0; i < drives.Count; ++i)
                drives[i] = ShipModules.FusionThruster;
            var jets = Sis.OwnJetSlots;
            for (int i = 0; i < jets.Count; ++i)
                jets[i] = ShipModules.TurningJets;
            int[] bomb = { 4, 5, 3, 1, 0, 1, 3, 4, 5, 2 };
            for (int i = 0; i < bomb.Length; ++i)
                modules[i] = (byte)(ShipModules.BombModule0 + bomb[i]);
        }
    }

    private void NotReady(int r)
    {
        if (r == 0)
            NPCPhrase("RETURN_WHEN_READY");
        else if (PlayerSaid(r, "further_assistance"))
        {
            NPCPhrase("NO_FURTHER_ASSISTANCE");

            DisablePhrase("further_assistance");
        }
        else if (PlayerSaid(r, "tech_help"))
        {
            NPCPhrase("USE_OUR_SHIPS_BEFORE");

            T.SetRaceAllied(ChmmrShip, true);
        }
        else if (PlayerSaid(r, "where_weapon"))
        {
            NPCPhrase("PRECURSOR_WEAPON");

            DisablePhrase("where_weapon");
        }
        else if (PlayerSaid(r, "where_distraction"))
        {
            NPCPhrase("PSYCHIC_WEAPONRY");

            DisablePhrase("where_distraction");
        }

        if (T.CheckAlliance(ChmmrShip) != Story.Allied.Good)
            Response("tech_help", NotReady);
        else if (PhraseEnabled("further_assistance"))
            Response("further_assistance", NotReady);
        if (PhraseEnabled("where_weapon") && GetGameState("UTWIG_BOMB_ON_SHIP") == 0)
            Response("where_weapon", NotReady);
        if (PhraseEnabled("where_distraction") && GetGameState("TALKING_PET_ON_SHIP") == 0)
            Response("where_distraction", NotReady);
        Response("bye", ExitConversation);
    }

    private void ImproveBomb(int r)
    {
        if (r == 0)
            NPCPhrase("WE_WILL_IMPROVE_BOMB");
        else if (PlayerSaid(r, "what_now"))
        {
            NPCPhrase("MODIFY_VESSEL");

            DisablePhrase("what_now");
        }
        else if (PlayerSaid(r, "wont_hurt_my_ship"))
        {
            NPCPhrase("WILL_DESTROY_IT");

            DisablePhrase("wont_hurt_my_ship");
        }
        else if (PlayerSaid(r, "bummer_about_my_ship"))
        {
            NPCPhrase("DEAD_SILENCE");

            DisablePhrase("bummer_about_my_ship");
        }
        else if (PlayerSaid(r, "other_assistance"))
        {
            NPCPhrase("USE_OUR_SHIPS_AFTER");

            T.SetRaceAllied(ChmmrShip, true);
        }

        if (PhraseEnabled("what_now"))
            Response("what_now", ImproveBomb);
        else if (PhraseEnabled("wont_hurt_my_ship"))
            Response("wont_hurt_my_ship", ImproveBomb);
        else if (PhraseEnabled("bummer_about_my_ship"))
            Response("bummer_about_my_ship", ImproveBomb);
        if (T.CheckAlliance(ChmmrShip) != Story.Allied.Good)
            Response("other_assistance", ImproveBomb);
        Response("proceed", ExitConversation);
    }

    private void ChmmrFree(int r)
    {
        if (r == 0 || PlayerSaid(r, "i_am_captain0") || PlayerSaid(r, "i_am_savior") || PlayerSaid(r, "i_am_silly"))
        {
            NPCPhrase("WHY_HAVE_YOU_FREED_US");
            T.AlienTalkSegue(Talk.WaitAll);
            SetGameState("CHMMR_EMERGING", 0);

            Response("serious_1", ChmmrFree);
            Response("serious_2", ChmmrFree);
            Response("silly", ChmmrFree);
        }
        else
        {
            NPCPhrase("WILL_HELP_ANALYZE_LOGS");

            if (GetGameState("AWARE_OF_SAMATRA") != 0)
                NPCPhrase("YOU_KNOW_SAMATRA");
            else
            {
                NPCPhrase("DONT_KNOW_ABOUT_SAMATRA");

                SetGameState("AWARE_OF_SAMATRA", 1);
            }

            if (GetGameState("TALKING_PET_ON_SHIP") != 0)
                NPCPhrase("HAVE_TALKING_PET");
            else
                NPCPhrase("NEED_DISTRACTION");

            if (GetGameState("UTWIG_BOMB_ON_SHIP") != 0)
                NPCPhrase("HAVE_BOMB");
            else
                NPCPhrase("NEED_WEAPON");

            if (GetGameState("TALKING_PET_ON_SHIP") == 0 || GetGameState("UTWIG_BOMB_ON_SHIP") == 0)
                NotReady(0);
            else
                ImproveBomb(0);
        }
    }

    private void ChmmrAdvice(int r)
    {
        if (PlayerSaid(r, "need_advice"))
            NPCPhrase("WHAT_ADVICE");
        else if (PlayerSaid(r, "how_defeat_urquan"))
        {
            NPCPhrase("DEFEAT_LIKE_SO");

            SetGameState("CHMMR_BOMB_STATE", 1);
            DisablePhrase("how_defeat_urquan");
        }
        else if (PlayerSaid(r, "what_about_tpet"))
        {
            NPCPhrase("SCARY_BUT_USEFUL");

            DisablePhrase("what_about_tpet");
        }
        else if (PlayerSaid(r, "what_about_bomb"))
        {
            NPCPhrase("ABOUT_BOMB");

            DisablePhrase("what_about_bomb");
        }
        else if (PlayerSaid(r, "what_about_sun_device"))
        {
            NPCPhrase("ABOUT_SUN_DEVICE");

            DisablePhrase("what_about_sun_device");
        }
        else if (PlayerSaid(r, "what_about_samatra"))
        {
            NPCPhrase("ABOUT_SAMATRA");

            DisablePhrase("what_about_samatra");
        }

        bool adviceLeft = false;
        if (PhraseEnabled("how_defeat_urquan"))
        {
            Response("how_defeat_urquan", ChmmrAdvice);
            adviceLeft = true;
        }
        if (PhraseEnabled("what_about_tpet") && GetGameState("TALKING_PET_ON_SHIP") != 0)
        {
            Response("what_about_tpet", ChmmrAdvice);
            adviceLeft = true;
        }
        if (PhraseEnabled("what_about_bomb") && GetGameState("UTWIG_BOMB_ON_SHIP") != 0)
        {
            Response("what_about_bomb", ChmmrAdvice);
            adviceLeft = true;
        }
        if (PhraseEnabled("what_about_sun_device") && GetGameState("SUN_DEVICE_ON_SHIP") != 0)
        {
            Response("what_about_sun_device", ChmmrAdvice);
            adviceLeft = true;
        }
        if (PhraseEnabled("what_about_samatra") && GetGameState("AWARE_OF_SAMATRA") != 0)
        {
            Response("what_about_samatra", ChmmrAdvice);
            adviceLeft = true;
        }
        Response("enough_advice", ChmmrShielded);

        if (!adviceLeft)
            DisablePhrase("need_advice");
    }

    private void ChmmrShielded(int r)
    {
        if (PlayerSaid(r, "find_out_whats_up"))
        {
            NPCPhrase("HYBRID_PROCESS");

            DisablePhrase("find_out_whats_up");
        }
        else if (PlayerSaid(r, "need_help"))
        {
            NPCPhrase("CANT_HELP");

            SetGameState("CHMMR_STACK", 1);
        }
        else if (PlayerSaid(r, "why_no_help"))
        {
            NPCPhrase("LONG_TIME");

            SetGameState("CHMMR_STACK", 2);
        }
        else if (PlayerSaid(r, "what_if_more_energy"))
        {
            NPCPhrase("DANGER_TO_US");

            SetGameState("CHMMR_STACK", 3);
        }
        else if (PlayerSaid(r, "enough_advice"))
            NPCPhrase("OK_ENOUGH_ADVICE");

        switch (GetGameState("CHMMR_STACK"))
        {
            case 0:
                Response("need_help", ChmmrShielded);
                break;
            case 1:
                Response("why_no_help", ChmmrShielded);
                break;
            case 2:
                Response("what_if_more_energy", ChmmrShielded);
                break;
        }
        if (PhraseEnabled("find_out_whats_up"))
            Response("find_out_whats_up", ChmmrShielded);
        if (PhraseEnabled("need_advice"))
            Response("need_advice", ChmmrAdvice);
        Response("bye_shielded", ExitConversation);
    }

    private void AfterBomb(int r)
    {
        if (PlayerSaid(r, "whats_up_after_bomb"))
        {
            if (GetGameState("CHMMR_STACK") != 0)
                NPCPhrase("GENERAL_INFO_AFTER_BOMB_2");
            else
            {
                NPCPhrase("GENERAL_INFO_AFTER_BOMB_1");

                SetGameState("CHMMR_STACK", 1);
            }

            DisablePhrase("whats_up_after_bomb");
        }
        else if (PlayerSaid(r, "what_do_after_bomb"))
        {
            NPCPhrase("DO_AFTER_BOMB");

            DisablePhrase("what_do_after_bomb");
        }

        if (PhraseEnabled("whats_up_after_bomb"))
            Response("whats_up_after_bomb", AfterBomb);
        if (PhraseEnabled("what_do_after_bomb"))
            Response("what_do_after_bomb", AfterBomb);
        Response("bye_after_bomb", ExitConversation);
    }

    // The greetings by visit (a BYTE): each switch's last case keeps its
    // count there; past the cases nothing is said and the count goes on.
    private int Greeting(int numVisits, params string[] hellos)
    {
        if (numVisits < hellos.Length)
            NPCPhrase(hellos[numVisits]);
        if (numVisits == hellos.Length - 1)
            --numVisits;
        return (byte)(numVisits + 1);
    }

    protected internal override void Intro()
    {
        int numVisits;

        if (GetGameState("CHMMR_BOMB_STATE") >= 2)
        {
            numVisits = Greeting(GetGameState("CHMMR_HOME_VISITS"), "HELLO_AFTER_BOMB_1", "HELLO_AFTER_BOMB_2");
            SetGameState("CHMMR_HOME_VISITS", numVisits);

            AfterBomb(0);
        }
        else if (GetGameState("CHMMR_UNLEASHED") != 0)
        {
            if (GetGameState("TALKING_PET_ON_SHIP") == 0 || GetGameState("UTWIG_BOMB_ON_SHIP") == 0)
                NotReady(0);
            else
            {
                NPCPhrase("YOU_ARE_READY");

                ImproveBomb(0);
            }
        }
        else
        {
            numVisits = GetGameState("CHMMR_HOME_VISITS");
            if (GetGameState("CHMMR_EMERGING") == 0)
            {
                // The shielded colour map.
                T.SetColorMap(1);
                numVisits = Greeting(numVisits, "WHY_YOU_HERE_1", "WHY_YOU_HERE_2", "WHY_YOU_HERE_3", "WHY_YOU_HERE_4");

                ChmmrShielded(0);
            }
            else
            {
                T.SetCommIntroMode(CommIntroMode.FadeInScreen, Talk.OneSecond * 2);
                NPCPhrase("WE_ARE_FREE");

                if (numVisits != 0)
                {
                    ChmmrFree(0);
                    numVisits = 0;
                }
                else
                {
                    NPCPhrase("WHO_ARE_YOU");

                    T.ConstructResponse("i_am_captain0", Sis.CaptainName, "i_am_captain1",
                        Sis.ShipName, "i_am_captain2");
                    T.DoResponsePhrase("i_am_captain0", ChmmrFree, Talk.SharedPhraseBuffer);
                    Response("i_am_savior", ChmmrFree);
                    Response("i_am_silly", ChmmrFree);
                }

                SetGameState("CHMMR_UNLEASHED", 1);
            }
            SetGameState("CHMMR_HOME_VISITS", numVisits);
        }
    }

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        T.SetSegue(Segue.Peace);
    }
}
