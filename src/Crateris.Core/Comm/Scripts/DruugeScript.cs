// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/druuge/druugec.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.World;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Druuge (druugec.c), whole: in space, at their trade world
/// (GLOBAL_FLAGS_AND_DATA bit 7) and guarding the Utwig's bomb at its moon
/// (bit 6, genutw.c's 5 ships). In space: their news (DRUUGE_SPACE_INFO),
/// or, with no escorts in the fleet (GetHeadLink (&amp;built_ship_q): the
/// host's, whoever answers, the stage 3b-2 plan's question 4) and no
/// weapon in the answering ship's own module slots, the ship taken for
/// salvage: a fight that doesn't count as an attack on them
/// (DRUUGE_SALVAGE). At the trade world: their news (DRUUGE_HOME_INFO),
/// the goods they scan aboard (the Maidens, the Mycon's egg cases, the
/// Burvixese broadcaster, the portal spawner: story flags, the group's),
/// and the trade. They sell for crew, the answering ship's
/// (DeltaSISGauges): a Mauler for 100 (AddEscortShips: the host's fleet
/// if it has room), the Rosy Sphere and their two fake artifacts for 100
/// each (story flags, the group's), 10 units of fuel for 10 (as much as
/// the answering ship's tanks hold), once slave trading has been explained
/// (KNOW_DRUUGE_SLAVERS); the crew sold in a talk (SlaveryCount) is added
/// to CREW_SOLD_TO_DRUUGE0 and 1 as the talk says goodbye, and past 100
/// and past 250 crew sold the crew's price at the starbase rises
/// (GLOBAL (CrewCost), by 7 and by 15 more: shared, as every price is).
/// They buy the goods for ships (the Maidens 6, an egg case 1, the spawner
/// 3) and fuel to the brim of the answering ship's own tanks (the
/// broadcaster and the spawner), or, while the players haven't the Rosy
/// Sphere, for the sphere alone. At the bomb's moon: their warnings
/// (BOMB_VISITS) and the fight for it. Their anger: a hostile ending that
/// wasn't their salvage sets ATTACKED_DRUUGE (post_druuge_enc), hostile
/// in space from then on; DRUUGE_MANNER (set as the bomb is taken, 3c)
/// makes them hostile everywhere. Every path is checked against UQM's own
/// script (tests/golden/talks-druuge.json).
/// </summary>
public sealed class DruugeScript : CommScript
{
    // races.h's DRUUGE_SHIP.
    private const int DruugeShip = 13;

    // druugec.c's prices, in crew; sis.h's fuel sizes.
    private const int ShipCrewCost = 100, ArtifactCrewCost = 100, FuelCrewCost = 10;
    private const uint FuelReserve = Hyperspace.Fuel.Reserve, FuelTankCapacity = Hyperspace.Fuel.TankSize,
        HeFuelTankCapacity = Hyperspace.Fuel.HighEfficiencyTankSize, FuelTankScale = Hyperspace.Fuel.Scale;

    // The script's statics: the crew sold in this talk (a COUNT), whether
    // the ship was taken for salvage, and the goods offered while the
    // sphere is traded for (init_druuge_comm resets the first two; the
    // third is set before it's read).
    private ushort _slaveryCount;
    private bool _attemptedSalvage;
    private int _lastResponse;

    public DruugeScript(Data.ConversationScript phrases) : base(phrases)
    {
    }

    private bool AtTradeWorld => (GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 7)) != 0;

    private bool AtBombPlanet => (GetGameState("GLOBAL_FLAGS_AND_DATA") & (1 << 6)) != 0;

    private void ExitConversation(int r)
    {
        if (PlayerSaid(r, "bye"))
        {
            T.SetSegue(Segue.Peace);

            if (AtTradeWorld)
            {
                if (_slaveryCount != 0)
                {
                    ushort previousSlaves = (ushort)(GetGameState("CREW_SOLD_TO_DRUUGE0")
                                                     | (GetGameState("CREW_SOLD_TO_DRUUGE1") << 8));
                    _slaveryCount += previousSlaves;
                    if (_slaveryCount > 250 && previousSlaves <= 250)
                    {
                        if (previousSlaves > 100)
                            T.SetCrewCost(T.CrewCost + (22 - 7));
                        else
                            T.SetCrewCost(T.CrewCost + 22);
                    }
                    else if (_slaveryCount > 100 && previousSlaves <= 100)
                        T.SetCrewCost(T.CrewCost + 7);

                    SetGameState("CREW_SOLD_TO_DRUUGE0", _slaveryCount & 0xFF);
                    SetGameState("CREW_SOLD_TO_DRUUGE1", _slaveryCount >> 8);
                }

                switch (GetGameState("DRUUGE_HOME_VISITS"))
                {
                    case 1:
                        NPCPhrase("BYE_FROM_TRADE_WORLD_1");
                        break;
                    default:
                        NPCPhrase("BYE_FROM_TRADE_WORLD_2");
                        break;
                }
            }
            else if (AtBombPlanet)
                NPCPhrase("GOODBYE_FROM_BOMB_PLANET");
            else
                NPCPhrase("GOODBYE_FROM_SPACE");
        }
        else /* if (R == then_we_take_bomb) */
        {
            T.SetSegue(Segue.Hostile);

            NPCPhrase("FIGHT_FOR_BOMB");
        }
    }

    // GLOBAL_SIS (CrewEnlisted) < cost: the answering ship's crew.
    private bool NotEnoughCrew(int cost) => Sis.Crew < cost;

    private void Buy(int r)
    {
        if (PlayerSaid(r, "want_to_buy") || PlayerSaid(r, "im_ready_to_buy"))
        {
            NPCPhrase("READY_TO_SELL");
            if (GetGameState("ROSY_SPHERE") == 0)
                NPCPhrase("HAVE_SPHERE");
            if (GetGameState("ARTIFACT_2_ON_SHIP") == 0)
                NPCPhrase("HAVE_ART_1");
            if (GetGameState("ARTIFACT_3_ON_SHIP") == 0)
                NPCPhrase("HAVE_ART_2");
            NPCPhrase("SHIPS_AND_FUEL");

            SetGameState("KNOW_DRUUGE_SLAVERS", 3);
        }
        else if (PlayerSaid(r, "buy_druuge_ship"))
        {
            if (NotEnoughCrew(ShipCrewCost))
                NPCPhrase("NOT_ENOUGH_CREW");
            else if (Sis.EscortRoom == 0)   // EscortFeasibilityStudy (DRUUGE_SHIP)
                NPCPhrase("NOT_ENOUGH_ROOM");
            else
            {
                T.DeltaSISGauges(-ShipCrewCost, 0, 0);
                _slaveryCount += ShipCrewCost;
                T.AddEscortShips(DruugeShip, 1);

                NPCPhrase("BOUGHT_SHIP");
            }
        }
        else if (PlayerSaid(r, "buy_rosy_sphere"))
        {
            if (NotEnoughCrew(ArtifactCrewCost))
                NPCPhrase("NOT_ENOUGH_CREW");
            else
            {
                T.DeltaSISGauges(-ArtifactCrewCost, 0, 0);
                _slaveryCount += ArtifactCrewCost;
                SetGameState("ROSY_SPHERE_ON_SHIP", 1);
                SetGameState("ROSY_SPHERE", 1);

                NPCPhrase("BOUGHT_SPHERE");
            }
        }
        else if (PlayerSaid(r, "buy_art_1"))
        {
            if (NotEnoughCrew(ArtifactCrewCost))
                NPCPhrase("NOT_ENOUGH_CREW");
            else
            {
                T.DeltaSISGauges(-ArtifactCrewCost, 0, 0);
                _slaveryCount += ArtifactCrewCost;
                SetGameState("ARTIFACT_2_ON_SHIP", 1);

                NPCPhrase("BOUGHT_ART_1");
            }
        }
        else if (PlayerSaid(r, "buy_art_2"))
        {
            if (NotEnoughCrew(ArtifactCrewCost))
                NPCPhrase("NOT_ENOUGH_CREW");
            else
            {
                T.DeltaSISGauges(-ArtifactCrewCost, 0, 0);
                _slaveryCount += ArtifactCrewCost;
                SetGameState("ARTIFACT_3_ON_SHIP", 1);

                NPCPhrase("BOUGHT_ART_2");
            }
        }
        else if (PlayerSaid(r, "buy_fuel"))
        {
            if (NotEnoughCrew(FuelCrewCost))
                NPCPhrase("NOT_ENOUGH_CREW");
            else
            {
                T.DeltaSISGauges(-FuelCrewCost, (int)(FuelCrewCost * FuelTankScale), 0);
                _slaveryCount += FuelCrewCost;

                NPCPhrase("BOUGHT_FUEL");
            }
        }

        Response("buy_druuge_ship", Buy);
        if (GetGameState("ROSY_SPHERE") == 0)
            Response("buy_rosy_sphere", Buy);
        if (GetGameState("ARTIFACT_2_ON_SHIP") == 0)
            Response("buy_art_1", Buy);
        if (GetGameState("ARTIFACT_3_ON_SHIP") == 0)
            Response("buy_art_2", Buy);
        Response("buy_fuel", Buy);
        Response("done_buying", TradeWorld);
    }

    private void Trade(int r)
    {
        if (!PlayerSaid(r, "whats_the_sphere_again"))
        {
            NPCPhrase("TRADE_FOR_SPHERE");
            _lastResponse = r;
        }
        else
        {
            NPCPhrase("SPHERE_IS");

            DisablePhrase("whats_the_sphere_again");
        }

        Response("no_way", Sell);
        Response("way", Sell);
        if (PhraseEnabled("whats_the_sphere_again"))
            Response("whats_the_sphere_again", Trade);
    }

    private bool HaveEggCase =>
        GetGameState("EGG_CASE0_ON_SHIP") != 0 || GetGameState("EGG_CASE1_ON_SHIP") != 0
        || GetGameState("EGG_CASE2_ON_SHIP") != 0;

    private void DoTransaction(int r)
    {
        if (PlayerSaid(r, "sell_maidens"))
            SetGameState("MAIDENS_ON_SHIP", 0);
        else if (PlayerSaid(r, "sell_fragments"))
        {
            if (GetGameState("EGG_CASE0_ON_SHIP") != 0)
                SetGameState("EGG_CASE0_ON_SHIP", 0);
            else if (GetGameState("EGG_CASE1_ON_SHIP") != 0)
                SetGameState("EGG_CASE1_ON_SHIP", 0);
            else if (GetGameState("EGG_CASE2_ON_SHIP") != 0)
                SetGameState("EGG_CASE2_ON_SHIP", 0);

            int numFrags = (byte)(GetGameState("FRAGMENTS_BOUGHT") + 1);
            SetGameState("FRAGMENTS_BOUGHT", numFrags);
        }
        else if (PlayerSaid(r, "sell_caster"))
            SetGameState("BURV_BROADCASTERS_ON_SHIP", 0);
        else if (PlayerSaid(r, "sell_spawner"))
            SetGameState("PORTAL_SPAWNER_ON_SHIP", 0);

        if (GetGameState("ROSY_SPHERE") == 0 && GetGameState("ROSY_SPHERE_ON_SHIP") != 0)
            SetGameState("ROSY_SPHERE", 1);
        else
        {
            bool tradeGas = false;
            int shipsToTrade = 0;
            int shipSlots = (byte)Sis.EscortRoom;   // EscortFeasibilityStudy (DRUUGE_SHIP), a BYTE here
            if (PlayerSaid(r, "sell_maidens"))
            {
                NPCPhrase("BOUGHT_MAIDENS");
                shipsToTrade = 6;
            }
            else if (PlayerSaid(r, "sell_fragments"))
            {
                NPCPhrase("BOUGHT_FRAGMENTS");
                shipsToTrade = 1;
            }
            else if (PlayerSaid(r, "sell_caster"))
            {
                NPCPhrase("BOUGHT_CASTER");
                shipsToTrade = 0;
                tradeGas = true;
            }
            else if (PlayerSaid(r, "sell_spawner"))
            {
                NPCPhrase("BOUGHT_SPAWNER");
                shipsToTrade = 3;
                tradeGas = true;
            }

            NPCPhrase("YOU_GET");
            if (shipsToTrade != 0)
            {
                T.AddEscortShips(DruugeShip, shipsToTrade);

                if (shipSlots >= shipsToTrade)
                    NPCPhrase("DEAL_FOR_STATED_SHIPS");
                else if (shipSlots == 0)
                    NPCPhrase("DEAL_FOR_NO_SHIPS");
                else
                    NPCPhrase("DEAL_FOR_LESS_SHIPS");

                if (tradeGas)
                    NPCPhrase("YOU_ALSO_GET");
            }

            if (tradeGas)
            {
                // The answering ship's own tanks, by its module slots (a
                // DWORD in UQM: with more fuel aboard than they hold, which
                // no ship has, it would wrap as UQM's does).
                uint capacity = FuelReserve;
                var slots = Sis.OwnModuleSlots;
                for (int slot = slots.Count - 1; slot >= 0; --slot)
                    if (slots[slot] is ShipModules.FuelTank or ShipModules.HighEfficiencyFuelSystem)
                        capacity += slots[slot] == ShipModules.FuelTank ? FuelTankCapacity : HeFuelTankCapacity;
                capacity -= Sis.Fuel;
                int f = (ushort)((capacity + (FuelTankScale >> 1)) / FuelTankScale);

                while (capacity > 0x3FFF)
                {
                    T.DeltaSISGauges(0, 0x3FFF, 0);
                    capacity -= 0x3FFF;
                }
                T.DeltaSISGauges(0, (short)capacity, 0);

                NPCPhrase("FUEL0");
                T.NPCNumber(f);
                NPCPhrase("FUEL1");

                if (f >= 250)
                    NPCPhrase("HIDEOUS_DEAL");
                else if (f >= 100)
                    NPCPhrase("BAD_DEAL");
                else if (f >= 50)
                    NPCPhrase("FAIR_DEAL");
                else if (f >= 10)
                    NPCPhrase("GOOD_DEAL");
                else
                    NPCPhrase("FINE_DEAL");
            }
        }
    }

    private void Sell(int r)
    {
        if (PlayerSaid(r, "want_to_sell"))
            NPCPhrase("READY_TO_BUY");
        else if (PlayerSaid(r, "no_way") || PlayerSaid(r, "way"))
        {
            if (PlayerSaid(r, "no_way"))
                NPCPhrase("OK_REGULAR_DEAL");
            else
            {
                NPCPhrase("OK_HERES_SPHERE");

                SetGameState("ROSY_SPHERE_ON_SHIP", 1);
            }

            DoTransaction(_lastResponse);
        }
        else if (PlayerSaid(r, "sell_maidens") || PlayerSaid(r, "sell_fragments")
                 || PlayerSaid(r, "sell_caster") || PlayerSaid(r, "sell_spawner"))
        {
            DoTransaction(r);
        }

        // While the players haven't the Rosy Sphere, each sale is offered for it first.
        Action<int> respFunc = GetGameState("ROSY_SPHERE") == 0 ? Trade : Sell;
        if (GetGameState("MAIDENS_ON_SHIP") != 0)
            Response("sell_maidens", respFunc);
        if (HaveEggCase && GetGameState("FRAGMENTS_BOUGHT") < 2)
            Response("sell_fragments", respFunc);
        if (GetGameState("BURV_BROADCASTERS_ON_SHIP") != 0)
            Response("sell_caster", respFunc);
        if (GetGameState("PORTAL_SPAWNER_ON_SHIP") != 0)
            Response("sell_spawner", respFunc);
        Response("done_selling", TradeWorld);
    }

    private void ExplainSlaveTrade(int r)
    {
        if (PlayerSaid(r, "want_to_buy"))
            NPCPhrase("WE_SELL_FOR_CREW");
        else if (PlayerSaid(r, "isnt_this_slave_trading"))
        {
            NPCPhrase("NO_SLAVE_TRADE");

            SetGameState("KNOW_DRUUGE_SLAVERS", 1);
        }
        else if (PlayerSaid(r, "what_do_with_crew"))
        {
            NPCPhrase("HAVE_FUN");

            SetGameState("KNOW_DRUUGE_SLAVERS", 2);
        }

        switch (GetGameState("KNOW_DRUUGE_SLAVERS"))
        {
            case 0:
                Response("isnt_this_slave_trading", ExplainSlaveTrade);
                break;
            case 1:
                Response("what_do_with_crew", ExplainSlaveTrade);
                break;
        }
        Response("i_will_never_trade_crew", TradeWorld);
        Response("im_ready_to_buy", Buy);
    }

    private void TradeWorld(int r)
    {
        if (PlayerSaid(r, "whats_up_at_trade_world"))
        {
            int numVisits = GetGameState("DRUUGE_HOME_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GEN_INFO_AT_TRADE_WORLD_1");
                    break;
                case 1:
                    NPCPhrase("GEN_INFO_AT_TRADE_WORLD_2");
                    break;
                case 2:
                    NPCPhrase("GEN_INFO_AT_TRADE_WORLD_3");
                    if (GetGameState("KNOW_ABOUT_SHATTERED") < 2)
                        SetGameState("KNOW_ABOUT_SHATTERED", 2);
                    break;
                case 3:
                    NPCPhrase("GEN_INFO_AT_TRADE_WORLD_4");
                    --numVisits;
                    break;
            }
            SetGameState("DRUUGE_HOME_INFO", numVisits);
            DisablePhrase("whats_up_at_trade_world");
        }
        else if (PlayerSaid(r, "done_selling"))
            NPCPhrase("OK_DONE_SELLING");
        else if (PlayerSaid(r, "done_buying"))
            NPCPhrase("OK_DONE_BUYING");
        else if (PlayerSaid(r, "i_will_never_trade_crew"))
            NPCPhrase("YOUR_LOSS");

        if (PhraseEnabled("whats_up_at_trade_world"))
            Response("whats_up_at_trade_world", TradeWorld);
        Response("want_to_sell", Sell);
        if (GetGameState("KNOW_DRUUGE_SLAVERS") == 3)
            Response("want_to_buy", Buy);
        else
            Response("want_to_buy", ExplainSlaveTrade);
        Response("bye", ExitConversation);
    }

    private void BombAmbush(int r)
    {
        if (PlayerSaid(r, "whats_up_at_bomb_planet"))
        {
            NPCPhrase("GEN_INFO_AT_BOMB_PLANET");
            SetGameState("BOMB_VISITS", 2);
        }
        else if (PlayerSaid(r, "we_get_bomb"))
        {
            NPCPhrase("NOT_GET_BOMB");
            SetGameState("BOMB_VISITS", 3);
        }

        switch (GetGameState("BOMB_VISITS"))
        {
            case 1:
                Response("whats_up_at_bomb_planet", BombAmbush);
                break;
            case 2:
                Response("we_get_bomb", BombAmbush);
                break;
            default:
                Response("then_we_take_bomb", ExitConversation);
                break;
        }
        Response("bye", ExitConversation);
    }

    private void Space(int r)
    {
        if (PlayerSaid(r, "whats_up_in_space"))
        {
            int numVisits = GetGameState("DRUUGE_SPACE_INFO");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("GENERAL_INFO_IN_SPACE_1");
                    break;
                case 1:
                    NPCPhrase("GENERAL_INFO_IN_SPACE_2");
                    break;
                case 2:
                    NPCPhrase("GENERAL_INFO_IN_SPACE_3");
                    break;
                case 3:
                    NPCPhrase("GENERAL_INFO_IN_SPACE_4");
                    --numVisits;
                    break;
            }
            SetGameState("DRUUGE_SPACE_INFO", numVisits);

            DisablePhrase("whats_up_in_space");
        }

        if (PhraseEnabled("whats_up_in_space"))
            Response("whats_up_in_space", Space);
        Response("bye", ExitConversation);
    }

    // A greeting that counts its visits: the first line, then the second from then on.
    private void Greet(string field, string first, string again)
    {
        int numVisits = GetGameState(field);
        switch (numVisits++)
        {
            case 0:
                NPCPhrase(first);
                break;
            case 1:
                NPCPhrase(again);
                --numVisits;
                break;
        }
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

        if (GetGameState("DRUUGE_MANNER") != 0)
        {
            if (AtTradeWorld)
                Greet("DRUUGE_HOME_VISITS", "HSTL_TRADE_WORLD_HELLO_1", "HSTL_TRADE_WORLD_HELLO_2");
            else
                Greet("DRUUGE_VISITS", "HOSTILE_SPACE_HELLO_1", "HOSTILE_SPACE_HELLO_2");

            T.SetSegue(Segue.Hostile);
        }
        else if (AtTradeWorld)
        {
            numVisits = GetGameState("DRUUGE_HOME_VISITS");
            switch (numVisits++)
            {
                case 0:
                    NPCPhrase("INITIAL_TRADE_WORLD_HELLO");
                    break;
                case 1:
                    NPCPhrase("SSQ_TRADE_WORLD_HELLO_1");
                    break;
                case 2:
                    NPCPhrase("SSQ_TRADE_WORLD_HELLO_2");
                    break;
                case 3:
                    NPCPhrase("SSQ_TRADE_WORLD_HELLO_3");
                    break;
                case 4:
                    NPCPhrase("SSQ_TRADE_WORLD_HELLO_4");
                    --numVisits;
                    break;
            }
            SetGameState("DRUUGE_HOME_VISITS", numVisits);

            if (GetGameState("ATTACKED_DRUUGE") != 0 && GetGameState("DRUUGE_DISCLAIMER") == 0)
            {
                // There is no HOSTILE_TRADE voice track that we know of
                // so this is currently disabled
                //NPCPhrase (HOSTILE_TRADE);
                SetGameState("DRUUGE_DISCLAIMER", 1);
            }
            if (GetGameState("MAIDENS_ON_SHIP") != 0 && GetGameState("SCANNED_MAIDENS") == 0)
            {
                NPCPhrase("SCAN_MAIDENS");
                SetGameState("SCANNED_MAIDENS", 1);
            }
            if (HaveEggCase && GetGameState("SCANNED_FRAGMENTS") == 0)
            {
                if (GetGameState("FRAGMENTS_BOUGHT") < 2)
                    NPCPhrase("SCAN_FRAGMENTS");
                else
                    NPCPhrase("ENOUGH_FRAGMENTS");
                SetGameState("SCANNED_FRAGMENTS", 1);
            }
            if (GetGameState("BURV_BROADCASTERS_ON_SHIP") != 0 && GetGameState("SCANNED_CASTER") == 0)
            {
                NPCPhrase("SCAN_DRUUGE_CASTER");
                SetGameState("SCANNED_CASTER", 1);
            }
            if (GetGameState("PORTAL_SPAWNER_ON_SHIP") != 0 && GetGameState("SCANNED_SPAWNER") == 0)
            {
                NPCPhrase("SCAN_ARILOU_SPAWNER");
                SetGameState("SCANNED_SPAWNER", 1);
            }

            TradeWorld(0);
        }
        else if (AtBombPlanet)
        {
            if (GetGameState("BOMB_VISITS") != 0)
                NPCPhrase("SUBSEQ_BOMB_WORLD_HELLO");
            else
            {
                NPCPhrase("INIT_BOMB_WORLD_HELLO");
                SetGameState("BOMB_VISITS", 1);
            }

            BombAmbush(0);
        }
        else if (GetGameState("ATTACKED_DRUUGE") != 0)
        {
            Greet("DRUUGE_VISITS", "HOSTILE_SPACE_HELLO_1", "HOSTILE_SPACE_HELLO_2");

            T.SetSegue(Segue.Hostile);
        }
        else
        {
            // A ship to salvage: no escorts (the host's fleet, whoever
            // answers) and no weapon in the answering ship's own module slots.
            bool salvage = false;
            if (T.EscortFleetEmpty)
            {
                salvage = true;
                foreach (byte whichModule in Sis.OwnModuleSlots)
                {
                    if (ShipModules.IsWeapon(whichModule))   // GUN_WEAPON to CANNON_WEAPON
                    {
                        salvage = false;
                        break;
                    }
                }
            }

            if (salvage)
            {
                Greet("DRUUGE_SALVAGE", "SALVAGE_YOUR_SHIP_1", "SALVAGE_YOUR_SHIP_2");

                T.SetSegue(Segue.Hostile);
                _attemptedSalvage = true;
            }
            else
            {
                Greet("DRUUGE_VISITS", "INIT_SPACE_HELLO", "SUBSEQUENT_SPACE_HELLO");

                Space(0);
            }
        }
    }

    // post_druuge_enc: a hostile ending that wasn't their salvage, while
    // they aren't hostile for good, is an attack on them (ATTACKED_DRUUGE,
    // their greetings in space counted afresh).
    protected internal override void PostEncounter()
    {
        if (T.Segue == Segue.Hostile && !_attemptedSalvage && GetGameState("DRUUGE_MANNER") == 0)
        {
            if (GetGameState("ATTACKED_DRUUGE") == 0)
            {
                SetGameState("ATTACKED_DRUUGE", 1);
                SetGameState("DRUUGE_VISITS", 0);
            }
        }
    }

    protected internal override void Init()
    {
        _slaveryCount = 0;
        _attemptedSalvage = false;

        // AlienTextBaseline and AlienTextWidth: in the description already.
        if ((GetGameState("DRUUGE_MANNER") == 0 && AtTradeWorld) || T.Activity == TalkActivity.WonLastBattle)
            T.SetSegue(Segue.Peace);
        else
            T.SetSegue(Segue.Hostile);
    }
}
