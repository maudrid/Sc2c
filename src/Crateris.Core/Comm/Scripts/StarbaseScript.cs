// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/starbas/starbas.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;
using Crateris.Core.Story;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The allied starbase commander's talk (starbas.c), all of it: "the
/// starbase is ready" (the opening's), his greetings and the news
/// bulletins by date (CheckBulletins), the new devices (DiscussDevices),
/// selling minerals into the talker's wallet, the information he gives
/// (the starbase, the history, the races, the mission, with his advice on
/// the flagship: AnalyzeCondition), and the goodbye.
/// <para>
/// Selling minerals sells each category at once, as UQM does when a
/// button is pressed; UQM counts them down a unit at a time on the screen
/// otherwise, to the same end, and the counting is the client's to draw.
/// </para>
/// </summary>
public sealed class StarbaseScript : CommScript
{
    /// <summary>races.h's numbers of the races the script names.</summary>
    private const int ArilouShip = 0, ChmmrShip = 1, HumanShip = 2, OrzShip = 3, ShofixtiShip = 5,
        SpathiShip = 6, SupoxShip = 7, UtwigShip = 9, ZoqfotpikShip = 19;

    /// <summary>NUM_GIFT_ARILOUS.</summary>
    private const int GiftArilous = 3;

    /// <summary>CREW_EXPENSE_THRESHOLD (sis.h).</summary>
    private const int CrewExpenseThreshold = 1000;

    /// <summary>FUEL_TANK_CAPACITY (sis.h), in 1/100 units.</summary>
    private const uint FuelTankCapacity = 50 * 100;

    /// <summary>NUM_DRIVE_SLOTS and NUM_JET_SLOTS (sis.h).</summary>
    private const int DriveSlots = 11, JetSlots = 8;

    /// <summary>START_YEAR (globdata.h).</summary>
    private const int StartYear = 2155;

    /// <summary>
    /// The devices DiscussDevices talks about, in globdata.h's order (the
    /// data plates are left out, as UQM leaves them): the field that says
    /// it's aboard (or ULTRON_CONDITION's value), the fields that say it's
    /// been discussed, and the line about it.
    /// </summary>
    private static readonly (string? OnShip, int Ultron, string[] Discussed, string Phrase)[] Devices =
    {
        ("PORTAL_SPAWNER_ON_SHIP", 0, new[] { "DISCUSSED_PORTAL_SPAWNER" }, "ABOUT_PORTAL"),
        ("TALKING_PET_ON_SHIP", 0, new[] { "DISCUSSED_TALKING_PET" }, "ABOUT_TALKPET"),
        ("UTWIG_BOMB_ON_SHIP", 0, new[] { "DISCUSSED_UTWIG_BOMB" }, "ABOUT_BOMB"),
        ("SUN_DEVICE_ON_SHIP", 0, new[] { "DISCUSSED_SUN_EFFICIENCY" }, "ABOUT_SUN"),
        ("ROSY_SPHERE_ON_SHIP", 0, new[] { "DISCUSSED_ROSY_SPHERE" }, "ABOUT_SPHERE"),
        ("AQUA_HELIX_ON_SHIP", 0, new[] { "DISCUSSED_AQUA_HELIX" }, "ABOUT_HELIX"),
        ("CLEAR_SPINDLE_ON_SHIP", 0, new[] { "DISCUSSED_CLEAR_SPINDLE" }, "ABOUT_SPINDLE"),
        (null, 1, new[] { "DISCUSSED_ULTRON" }, "ABOUT_ULTRON_0"),
        (null, 2, new[] { "DISCUSSED_ULTRON" }, "ABOUT_ULTRON_1"),
        (null, 3, new[] { "DISCUSSED_ULTRON" }, "ABOUT_ULTRON_2"),
        (null, 4, new[] { "DISCUSSED_ULTRON" }, "ABOUT_ULTRON_3"),
        ("MAIDENS_ON_SHIP", 0, new[] { "DISCUSSED_MAIDENS" }, "ABOUT_MAIDENS"),
        ("UMGAH_BROADCASTERS_ON_SHIP", 0, new[] { "DISCUSSED_UMGAH_HYPERWAVE" }, "ABOUT_UCASTER"),
        ("BURV_BROADCASTERS_ON_SHIP", 0, new[] { "DISCUSSED_BURVIX_HYPERWAVE" }, "ABOUT_BCASTER"),
        ("TAALO_PROTECTOR_ON_SHIP", 0, new[] { "DISCUSSED_TAALO_PROTECTOR" }, "ABOUT_SHIELD"),
        ("EGG_CASE0_ON_SHIP", 0, Eggs("DISCUSSED_EGG_CASING0"), "ABOUT_EGGCASE_0"),
        ("EGG_CASE1_ON_SHIP", 0, Eggs("DISCUSSED_EGG_CASING1"), "ABOUT_EGGCASE_0"),
        ("EGG_CASE2_ON_SHIP", 0, Eggs("DISCUSSED_EGG_CASING2"), "ABOUT_EGGCASE_0"),
        ("SYREEN_SHUTTLE_ON_SHIP", 0, new[] { "DISCUSSED_SYREEN_SHUTTLE" }, "ABOUT_SHUTTLE"),
        ("VUX_BEAST_ON_SHIP", 0, new[] { "DISCUSSED_VUX_BEAST" }, "ABOUT_VUXBEAST0"),
        ("DESTRUCT_CODE_ON_SHIP", 0, new[] { "DISCUSSED_DESTRUCT_CODE" }, "ABOUT_DESTRUCT"),
        ("PORTAL_KEY_ON_SHIP", 0, new[] { "DISCUSSED_URQUAN_WARP" }, "ABOUT_WARPPOD"),
        ("ARTIFACT_2_ON_SHIP", 0, new[] { "DISCUSSED_ARTIFACT_2" }, "ABOUT_ARTIFACT_2"),
        ("ARTIFACT_3_ON_SHIP", 0, new[] { "DISCUSSED_ARTIFACT_3" }, "ABOUT_ARTIFACT_3"),
    };

    // The egg casings: the one checked first, then all three set, in
    // UQM's order (0, 1, 2).
    private static string[] Eggs(string first) =>
        new[] { first, "DISCUSSED_EGG_CASING0", "DISCUSSED_EGG_CASING1", "DISCUSSED_EGG_CASING2" };

    // CurBulletinMask: the bulletins given this talk.
    private uint _curBulletinMask;

    // stack0 to stack3, which TellHistory, TellMission and TellStarBase
    // share; and each list's own mask (static in UQM, reset as it starts).
    private int _stack0, _stack1, _stack2, _stack3;
    private int _allianceMask, _hierarchyMask, _racesMask, _warMask, _ancientMask, _defeatMask;

    // buf0: TellStarBase's own constructed reply.
    private readonly PhraseBuffer _buf0 = new();

    public StarbaseScript(ConversationScript phrases) : base(phrases)
    {
    }

    private void ByeBye(int r)
    {
        _curBulletinMask |= T.Get32("STARBASE_BULLETS0");
        T.Set32("STARBASE_BULLETS0", _curBulletinMask);

        if (GetGameState("CHMMR_BOMB_STATE") >= 2)
            NPCPhrase("GOOD_LUCK_AGAIN");
        else
        {
            char letter = "ABCDEFGH"[(byte)T.Random() & 7];
            NPCPhrase($"NORMAL_GOODBYE_{letter}0");
            if (!T.UsingSpeech)
            {
                NPCPhrase("SPACE");
                NPCPhrase(Talk.GlobalPlayerName);
            }
            NPCPhrase($"NORMAL_GOODBYE_{letter}1");
        }
    }

    private void AllianceInfo(int r)
    {
        // The masks' bits, in the order UQM checks the replies said.
        string[] races = { "shofixti", "yehat", "arilou", "chenjesu", "mmrnmhrm", "syreen" };
        if (PlayerSaid(r, "what_about_alliance"))
        {
            NPCPhrase("WHICH_ALLIANCE");
            _allianceMask = 0;
        }
        else
            for (int i = 0; i < races.Length; ++i)
                if (PlayerSaid(r, races[i]))
                {
                    NPCPhrase("ABOUT_" + races[i].ToUpperInvariant());
                    _allianceMask |= 1 << i;
                    break;
                }

        for (int i = 0; i < races.Length; ++i)
            if ((_allianceMask & (1 << i)) == 0)
                Response(races[i], AllianceInfo);
        Response("enough_alliance", AlienRaces);
    }

    private void HierarchyInfo(int r)
    {
        // HIERARCHY_MYCON is bit 0, ..., HIERARCHY_URQUAN bit 6; UQM checks
        // and offers the Ur-Quan first.
        string[] bits = { "mycon", "spathi", "umgah", "androsynth", "ilwrath", "vux", "urquan" };
        string[] order = { "urquan", "mycon", "spathi", "umgah", "androsynth", "ilwrath", "vux" };
        if (PlayerSaid(r, "what_about_hierarchy"))
        {
            NPCPhrase("WHICH_HIERARCHY");
            _hierarchyMask = 0;
        }
        else
            foreach (var race in order)
                if (PlayerSaid(r, race))
                {
                    NPCPhrase("ABOUT_" + race.ToUpperInvariant());
                    _hierarchyMask |= 1 << Array.IndexOf(bits, race);
                    break;
                }

        foreach (var race in order)
            if ((_hierarchyMask & (1 << Array.IndexOf(bits, race))) == 0)
                Response(race, HierarchyInfo);
        Response("enough_hierarchy", AlienRaces);
    }

    private void AlienRaces(int r)
    {
        const int Alliance = 1 << 0, Hierarchy = 1 << 1, Other = 1 << 2;
        if (PlayerSaid(r, "alien_races"))
        {
            NPCPhrase("WHICH_ALIEN");
            _racesMask = 0;
        }
        else if (PlayerSaid(r, "enough_alliance"))
        {
            NPCPhrase("OK_ENOUGH_ALLIANCE");
            _racesMask |= Alliance;
        }
        else if (PlayerSaid(r, "enough_hierarchy"))
        {
            NPCPhrase("OK_ENOUGH_HIERARCHY");
            _racesMask |= Hierarchy;
        }
        else if (PlayerSaid(r, "what_about_other"))
        {
            NPCPhrase("ABOUT_OTHER");
            _racesMask |= Other;
        }

        if ((_racesMask & Alliance) == 0)
            Response("what_about_alliance", AllianceInfo);
        if ((_racesMask & Hierarchy) == 0)
            Response("what_about_hierarchy", HierarchyInfo);
        if ((_racesMask & Other) == 0)
            Response("what_about_other", AlienRaces);
        Response("enough_aliens", TellHistory);
    }

    private void WarInfo(int r)
    {
        (string Reply, string Line)[] items =
        {
            ("what_started_war", "URQUAN_STARTED_WAR"), ("what_was_war_like", "WAR_WAS_LIKE_SO"),
            ("why_lose_war", "LOST_WAR_BECAUSE"), ("what_after_war", "AFTER_WAR"),
        };
        Listed(r, "the_war", "WHICH_WAR", ref _warMask, items, WarInfo, "enough_war", TellHistory);
    }

    private void AncientHistory(int r)
    {
        (string Reply, string Line)[] items =
        {
            ("precursors", "ABOUT_PRECURSORS"), ("old_races", "ABOUT_OLD_RACES"),
            ("aliens_on_earth", "ABOUT_ALIENS_ON_EARTH"),
        };
        Listed(r, "ancient_history", "WHICH_ANCIENT", ref _ancientMask, items, AncientHistory, "enough_ancient",
            TellHistory);
    }

    private void DefeatUrquan(int r)
    {
        (string Reply, string Line)[] items =
        {
            ("how_find_urquan", "FIND_URQUAN"), ("how_fight_urquan", "FIGHT_URQUAN"),
            ("how_ally", "ALLY_LIKE_SO"), ("how_get_strong", "STRONG_LIKE_SO"),
        };
        Listed(r, "how_defeat", "DEFEAT_LIKE_SO", ref _defeatMask, items, DefeatUrquan, "enough_defeat", TellMission);
    }

    // WarInfo, AncientHistory and DefeatUrquan: a list of replies, each
    // taken away once asked (UQM's masks), checked and offered in one
    // order, its bits in that order.
    private void Listed(int r, string start, string startLine, ref int mask,
        (string Reply, string Line)[] items, Action<int> func, string enough, Action<int> enoughFunc)
    {
        if (PlayerSaid(r, start))
        {
            NPCPhrase(startLine);
            mask = 0;
        }
        else
            for (int i = 0; i < items.Length; ++i)
                if (PlayerSaid(r, items[i].Reply))
                {
                    NPCPhrase(items[i].Line);
                    mask |= 1 << i;
                    break;
                }

        for (int i = 0; i < items.Length; ++i)
            if ((mask & (1 << i)) == 0)
                Response(items[i].Reply, func);
        Response(enough, enoughFunc);
    }

    private void TellHistory(int r)
    {
        if (PlayerSaid(r, "history"))
        {
            NPCPhrase("WHICH_HISTORY");
            _stack0 = _stack1 = _stack2 = 0;
        }
        else if (PlayerSaid(r, "enough_aliens"))
        {
            NPCPhrase("OK_ENOUGH_ALIENS");
            _stack0 = 1;
        }
        else if (PlayerSaid(r, "enough_war"))
        {
            NPCPhrase("OK_ENOUGH_WAR");
            _stack1 = 1;
        }
        else if (PlayerSaid(r, "enough_ancient"))
        {
            NPCPhrase("OK_ENOUGH_ANCIENT");
            _stack2 = 1;
        }

        if (_stack0 == 0)
            Response("alien_races", AlienRaces);
        if (_stack1 == 0)
            Response("the_war", WarInfo);
        if (_stack2 == 0)
            Response("ancient_history", AncientHistory);
        Response("enough_history", NeedInfo);
    }

    // AnalyzeCondition: the commander's advice on the flagship, the allies
    // and the fleet.
    private void AnalyzeCondition()
    {
        int thrusters = Sis.Thrusters, jets = Sis.Jets, guns = 0, bays = 0, batts = 0, track = 0, defense = 0;
        foreach (var module in Sis.ModuleSlots)
            switch (module)
            {
                case "STORAGE_BAY":
                    ++bays;
                    break;
                case "DYNAMO_UNIT":
                    batts += 1;
                    break;
                case "SHIVA_FURNACE":
                    batts += 2;
                    break;
                case "GUN_WEAPON":
                    guns += 1;
                    break;
                case "BLASTER_WEAPON":
                    guns += 2;
                    break;
                case "CANNON_WEAPON":
                    guns += 3;
                    break;
                case "TRACKING_SYSTEM":
                    ++track;
                    break;
                case "ANTIMISSILE_DEFENSE":
                    ++defense;
                    break;
            }
        if (track != 0 && guns != 0)
            guns += 2;

        int crew = Sis.Crew, landers = Sis.Landers;
        uint fuel = Sis.Fuel;
        bool hasMinimum = thrusters >= 7 && jets >= 5 && crew >= Data.ShipLoadout.CrewPodCapacity
            && fuel >= FuelTankCapacity && bays >= 1 && landers != 0 && batts >= 1 && guns >= 2;
        NPCPhrase("LETS_SEE");
        if (!hasMinimum && GetGameState("CHMMR_BOMB_STATE") < 2)
        {
            NPCPhrase("IMPROVE_1");
            if (thrusters < 7)
                NPCPhrase("NEED_THRUSTERS_1");
            if (jets < 5)
                NPCPhrase("NEED_TURN_1");
            if (guns < 2)
                NPCPhrase("NEED_GUNS_1");
            if (crew < Data.ShipLoadout.CrewPodCapacity)
                NPCPhrase("NEED_CREW_1");
            if (fuel < FuelTankCapacity)
                NPCPhrase("NEED_FUEL_1");
            if (bays < 1)
                NPCPhrase("NEED_STORAGE_1");
            if (landers == 0)
                NPCPhrase("NEED_LANDERS_2");
            if (batts < 1)
                NPCPhrase("NEED_DYNAMOS_1");

            NPCPhrase(Sis.ResourceUnits >= 3000 ? "IMPROVE_FLAGSHIP_WITH_RU" : "GO_GET_MINERALS");
            return;
        }

        // CalculateEscortsWorth's COUNT.
        int fleetStrength = (ushort)Sis.EscortsWorth;
        int aliens = 0;
        for (int i = 0; i < T.Story.Alliances.Count; ++i)
            if (i != HumanShip && T.CheckAlliance(i) == Story.Allied.Good)
                ++aliens;

        bool hasMaximum = thrusters == DriveSlots && jets == JetSlots
            && crew >= Data.ShipLoadout.CrewPodCapacity * 3 && fuel >= FuelTankCapacity * 3
            && landers >= 3 && batts >= 4 && guns >= 7 && defense >= 2;
        NPCPhrase(!hasMaximum && GetGameState("CHMMR_BOMB_STATE") < 2 ? "GOT_OK_FLAGSHIP" : "GOT_AWESOME_FLAGSHIP");

        if (GetGameState("CHMMR_BOMB_STATE") >= 2)
        {
            NPCPhrase("CHMMR_IMPROVED_BOMB");
            if (fleetStrength < 20000)
                NPCPhrase("MUST_ACQUIRE_AWESOME_FLEET");
            else
            {
                NPCPhrase("GOT_AWESOME_FLEET");
                NPCPhrase(GetGameState("TALKING_PET_ON_SHIP") == 0 ? "MUST_ELIMINATE_URQUAN_GUARDS" : "GO_DESTROY_SAMATRA");
            }
        }
        else if (aliens < 2)
            NPCPhrase("GO_ALLY_WITH_ALIENS");
        else
        {
            NPCPhrase("MADE_SOME_ALLIES");
            if (fleetStrength < 6000)
                NPCPhrase(Sis.ResourceUnits >= 3000 ? "BUY_COMBAT_SHIPS" : "GET_SHIPS_BY_MINING_OR_ALLIANCE");
            else
            {
                NPCPhrase("GOT_OK_FLEET");
                if (!hasMaximum)
                {
                    NPCPhrase("IMPROVE_2");
                    if (thrusters < DriveSlots)
                        NPCPhrase("NEED_THRUSTERS_2");
                    if (jets < JetSlots)
                        NPCPhrase("NEED_TURN_2");
                    if (guns < 7)
                        NPCPhrase("NEED_GUNS_2");
                    if (crew < Data.ShipLoadout.CrewPodCapacity * 3)
                        NPCPhrase("NEED_CREW_2");
                    if (fuel < FuelTankCapacity * 3)
                        NPCPhrase("NEED_FUEL_2");
                    if (landers < 3)
                        NPCPhrase("NEED_LANDERS_1");
                    if (batts < 4)
                        NPCPhrase("NEED_DYNAMOS_2");
                    if (defense < 2)
                        NPCPhrase("NEED_POINT");
                }
                else if (GetGameState("AWARE_OF_SAMATRA") == 0)
                    NPCPhrase("GO_LEARN_ABOUT_URQUAN");
                else
                {
                    NPCPhrase("KNOW_ABOUT_SAMATRA");
                    if (GetGameState("UTWIG_BOMB") == 0)
                        NPCPhrase("FIND_WAY_TO_DESTROY_SAMATRA");
                    else if (GetGameState("UTWIG_BOMB_ON_SHIP") != 0)
                        NPCPhrase("MUST_INCREASE_BOMB_STRENGTH");
                }
            }
        }
    }

    private void TellMission(int r)
    {
        if (PlayerSaid(r, "our_mission"))
        {
            NPCPhrase("WHICH_MISSION");
            _stack0 = _stack1 = _stack2 = _stack3 = 0;
        }
        else if (PlayerSaid(r, "where_get_minerals"))
        {
            NPCPhrase("GET_MINERALS");
            _stack0 = 1;
        }
        else if (PlayerSaid(r, "what_about_aliens"))
        {
            NPCPhrase("ABOUT_ALIENS");
            _stack1 = 1;
        }
        else if (PlayerSaid(r, "what_do_now"))
        {
            AnalyzeCondition();
            _stack2 = 1;
        }
        else if (PlayerSaid(r, "what_about_urquan"))
        {
            NPCPhrase("MUST_DEFEAT");
            _stack3 = 1;
        }
        else if (PlayerSaid(r, "enough_defeat"))
        {
            NPCPhrase("OK_ENOUGH_DEFEAT");
            _stack3 = 2;
        }

        if (_stack0 == 0)
            Response("where_get_minerals", TellMission);
        if (_stack1 == 0)
            Response("what_about_aliens", TellMission);
        if (_stack2 == 0)
            Response("what_do_now", TellMission);
        if (_stack3 == 0)
            Response("what_about_urquan", TellMission);
        else if (_stack3 == 1)
            Response("how_defeat", DefeatUrquan);

        Response("enough_mission", NeedInfo);
    }

    private void TellStarBase(int r)
    {
        if (PlayerSaid(r, "starbase_functions"))
        {
            NPCPhrase("WHICH_FUNCTION");
            _stack0 = _stack1 = _stack2 = _stack3 = 0;
        }
        else if (PlayerSaid(r, "tell_me_about_fuel0"))
        {
            NPCPhrase("ABOUT_FUEL");
            _stack1 = 1;
        }
        else if (PlayerSaid(r, "tell_me_about_crew"))
        {
            NPCPhrase("ABOUT_CREW0");
            if (T.UsingSpeech)
                NPCPhrase("YOUR_FLAGSHIP_3DO2");
            else
            {
                NPCPhrase("YOUR_FLAGSHIP_PC");
                NPCPhrase(Talk.GlobalShipName);
            }
            NPCPhrase("ABOUT_CREW1");
            _stack2 = 2;
        }
        else if (PlayerSaid(r, "tell_me_about_modules0"))
        {
            NPCPhrase("ABOUT_MODULES");
            _stack0 = 1;
        }
        else if (PlayerSaid(r, "tell_me_about_ships"))
        {
            NPCPhrase("ABOUT_SHIPS");
            _stack2 = 1;
        }
        else if (PlayerSaid(r, "tell_me_about_ru"))
        {
            NPCPhrase("ABOUT_RU");
            _stack3 = 1;
        }
        else if (PlayerSaid(r, "tell_me_about_minerals"))
        {
            NPCPhrase("ABOUT_MINERALS");
            _stack3 = 2;
        }
        else if (PlayerSaid(r, "tell_me_about_life"))
        {
            NPCPhrase("ABOUT_LIFE");
            _stack3 = 3;
        }

        if (_stack0 == 0)
            T.ConstructResponse(_buf0, "tell_me_about_modules0", Sis.ShipName, "tell_me_about_modules1");
        if (_stack1 == 0)
            T.ConstructResponse("tell_me_about_fuel0", Sis.ShipName, "tell_me_about_fuel1");
        string? third = _stack2 switch { 0 => "tell_me_about_ships", 1 => "tell_me_about_crew", _ => null };
        string? fourth = _stack3 switch
        {
            0 => "tell_me_about_ru",
            1 => "tell_me_about_minerals",
            2 => "tell_me_about_life",
            _ => null,
        };

        if (_stack0 == 0)
            T.DoResponsePhrase("tell_me_about_modules0", TellStarBase, _buf0);
        if (_stack1 == 0)
            T.DoResponsePhrase("tell_me_about_fuel0", TellStarBase, Talk.SharedPhraseBuffer);
        if (third is not null)
            Response(third, TellStarBase);
        if (fourth is not null)
            Response(fourth, TellStarBase);

        Response("enough_starbase", NeedInfo);
    }

    private void NeedInfo(int r)
    {
        if (PlayerSaid(r, "need_info"))
            NPCPhrase("WHAT_KIND_OF_INFO");
        else if (PlayerSaid(r, "enough_starbase"))
            NPCPhrase("OK_ENOUGH_STARBASE");
        else if (PlayerSaid(r, "enough_history"))
            NPCPhrase("OK_ENOUGH_HISTORY");
        else if (PlayerSaid(r, "enough_mission"))
            NPCPhrase("OK_ENOUGH_MISSION");

        Response("starbase_functions", TellStarBase);
        Response("history", TellHistory);
        Response("our_mission", TellMission);
        Response("no_need_info", NormalStarbase);
    }

    // DiscussDevices: whether there's a device aboard not yet discussed,
    // and with talkAbout, the commander talks about each (and marks them
    // discussed; without, UQM writes them not discussed, as here).
    private bool DiscussDevices(bool talkAbout)
    {
        if (talkAbout)
            NPCPhrase("DEVICE_HEAD");
        int phraseIndex = 2, vuxBeastIndex = 0;
        foreach (var d in Devices)
        {
            bool aboard = d.OnShip is { } on ? GetGameState(on) != 0 : GetGameState("ULTRON_CONDITION") == d.Ultron;
            if (!aboard || GetGameState(d.Discussed[0]) != 0)
                continue;
            for (int i = d.Discussed.Length > 1 ? 1 : 0; i < d.Discussed.Length; ++i)
                SetGameState(d.Discussed[i], talkAbout ? 1 : 0);
            if (talkAbout)
            {
                if (phraseIndex > 2)
                    NPCPhrase("BETWEEN_DEVICES");
                NPCPhrase(d.Phrase);
                if (d.Phrase == "ABOUT_VUXBEAST0")
                {
                    vuxBeastIndex = ++phraseIndex;
                    NPCPhrase("ABOUT_VUXBEAST1");
                }
            }
            phraseIndex += 2;
        }

        if (talkAbout)
        {
            NPCPhrase("DEVICE_TAIL");
            if (vuxBeastIndex != 0)
            {
                // The tracks up to the Vux beast's scientist's report, then
                // the report without the commander's mouth moving, then the rest.
                T.AlienTalkSegue(vuxBeastIndex - 1);
                T.EnableTalkingAnim(false);
                T.AlienTalkSegue(vuxBeastIndex);
                T.EnableTalkingAnim(true);
                T.AlienTalkSegue(Talk.WaitAll);
            }
        }
        return phraseIndex > 2;
    }

    // CheckTiming: whether the date is past the starbase's day (the day it
    // was ready) and this many months and days. Its year counts from
    // START_YEAR, whatever year the starbase was ready, as UQM's does.
    private bool CheckTiming(int monthIndex, int dayIndex)
    {
        int[] daysInMonth = { 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31 };
        int mi = GetGameState("STARBASE_MONTH");
        if (mi < 1 || mi > 12)
            throw new InvalidOperationException($"STARBASE_MONTH is {mi}: the bulletins come after the starbase is ready");
        int yearIndex = StartYear;

        dayIndex += GetGameState("STARBASE_DAY");
        while (dayIndex > daysInMonth[mi - 1])
        {
            dayIndex -= daysInMonth[mi - 1];
            if (++mi > 12)
            {
                mi = 1;
                ++yearIndex;
            }
        }

        monthIndex += mi;
        yearIndex += (monthIndex - 1) / 12;
        monthIndex = (monthIndex - 1) % 12 + 1;

        var c = T.Clock;
        return yearIndex < c.Year
            || (yearIndex == c.Year && (monthIndex < c.Month || (monthIndex == c.Month && dayIndex < c.Day)));
    }

    private int CrewSoldToDruuge => GetGameState("CREW_SOLD_TO_DRUUGE0") | (GetGameState("CREW_SOLD_TO_DRUUGE1") << 8);

    private void CheckBulletins(bool repeat)
    {
        uint bulletinMask = repeat ? _curBulletinMask ^ 0xFFFFFFFFu : T.Get32("STARBASE_BULLETS0");
        string? intro = null;
        for (int b0 = 0; b0 < 32; ++b0)
        {
            if ((bulletinMask & (1u << b0)) != 0)
                continue;
            string? line = null;

            // A bulletin that will never be due is marked given.
            void Never() => bulletinMask |= 1u << b0;

            switch (b0)
            {
                case 0:
                    line = IsAllied(SpathiShip) ? "STARBASE_BULLETIN_1" : null;
                    break;
                case 1:
                    line = IsAllied(ZoqfotpikShip) ? "STARBASE_BULLETIN_2" : null;
                    break;
                case 2:
                    line = IsAllied(SupoxShip) ? "STARBASE_BULLETIN_3" : null;
                    break;
                case 3:
                    line = IsAllied(UtwigShip) ? "STARBASE_BULLETIN_4" : null;
                    break;
                case 4:
                    line = IsAllied(OrzShip) ? "STARBASE_BULLETIN_5" : null;
                    break;
                case 5:
                    if (GetGameState("ARILOU_MANNER") == 2)
                        Never();
                    else if (GetGameState("PORTAL_SPAWNER") != 0 && (repeat || Sis.EscortRoom != 0))
                    {
                        line = "STARBASE_BULLETIN_6";
                        if (!repeat)
                            T.AddEscortShips(ArilouShip, GiftArilous);
                    }
                    break;
                case 6:
                    line = GetGameState("ZOQFOT_DISTRESS") == 1 ? "STARBASE_BULLETIN_7" : null;
                    break;
                case 7:
                    if (GetGameState("MET_MELNORME") != 0)
                        Never();
                    else if (CheckTiming(3, 0))
                        line = "STARBASE_BULLETIN_8";
                    break;
                case 8:
                    if (GetGameState("MET_MELNORME") != 0)
                        Never();
                    else if (CheckTiming(6, 0))
                        line = "STARBASE_BULLETIN_9";
                    break;
                case 9:
                    if (GetGameState("FOUND_PLUTO_SPATHI") != 0)
                        Never();
                    else if (CheckTiming(0, 7))
                        line = "STARBASE_BULLETIN_10";
                    break;
                case 10:
                    line = GetGameState("SPATHI_SHIELDED_SELVES") != 0 ? "STARBASE_BULLETIN_11" : null;
                    break;
                case 11:
                    if (GetGameState("ZOQFOT_HOME_VISITS") != 0 || T.Get32("ZOQFOT_GRPOFFS0") != 0)
                        Never();
                    else if (CheckTiming(0, 42))
                        line = "STARBASE_BULLETIN_12";
                    break;
                case 12:
                    line = IsAllied(ChmmrShip) ? "STARBASE_BULLETIN_13" : null;
                    break;
                case 13:
                    line = IsAllied(ShofixtiShip) ? "STARBASE_BULLETIN_14" : null;
                    break;
                case 14:
                    line = GetGameState("PKUNK_MISSION") != 0 ? "STARBASE_BULLETIN_15" : null;
                    break;
                case 15:
                    if (GetGameState("DESTRUCT_CODE_ON_SHIP") != 0)
                        Never();
                    else if (CheckTiming(7, 0))
                        line = "STARBASE_BULLETIN_16";
                    break;
                case 17:
                    line = GetGameState("YEHAT_ABSORBED_PKUNK") != 0 ? "STARBASE_BULLETIN_18" : null;
                    break;
                case 18:
                    line = GetGameState("CHMMR_BOMB_STATE") == 2 ? "STARBASE_BULLETIN_19" : null;
                    break;
                case 21:
                    line = GetGameState("ZOQFOT_DISTRESS") == 2 ? "STARBASE_BULLETIN_22" : null;
                    break;
                case 26:
                    if (CrewSoldToDruuge > 100)
                        Never();
                    else if (CrewSoldToDruuge != 0)
                        line = "STARBASE_BULLETIN_27";
                    break;
                case 27:
                    if (CrewSoldToDruuge > 250)
                        Never();
                    else if (CrewSoldToDruuge > 100)
                        line = "STARBASE_BULLETIN_28";
                    break;
                case 28:
                    int crewBought = GetGameState("CREW_PURCHASED0") | (GetGameState("CREW_PURCHASED1") << 8);
                    line = crewBought >= CrewExpenseThreshold ? "STARBASE_BULLETIN_29" : null;
                    break;
                case 29:
                    line = CrewSoldToDruuge > 250 ? "STARBASE_BULLETIN_30" : null;
                    break;
            }

            if (line is null)
                continue;
            if (intro is not null)
                NPCPhrase("BETWEEN_BULLETINS");
            else if (repeat)
                intro = "BEFORE_WE_GO_ON_1";
            else
            {
                intro = $"BEFORE_WE_GO_ON_{Math.Min((byte)T.Random() % 7, 6) + 1}";
                NPCPhrase(intro);
            }
            NPCPhrase(line);
            _curBulletinMask |= 1u << b0;
        }

        if (intro is null && GetGameState("STARBASE_VISITED") != 0)
            NPCPhrase("RETURN_HELLO");
        else if (!repeat)
            T.Set32("STARBASE_BULLETS0", bulletinMask);
    }

    private bool IsAllied(int race) => T.CheckAlliance(race) == Story.Allied.Good;

    private void NormalStarbase(int r)
    {
        if (PlayerSaid(r, "no_need_info"))
            NPCPhrase("OK_NO_NEED_INFO");
        else if (PlayerSaid(r, "new_devices"))
            DiscussDevices(true);
        else if (PlayerSaid(r, "repeat_bulletins"))
            CheckBulletins(true);
        else if (r == 0)
        {
            if (GetGameState("MOONBASE_ON_SHIP") != 0)
            {
                NPCPhrase("STARBASE_IS_READY_A");
                if (T.UsingSpeech)
                    NPCPhrase("YOUR_FLAGSHIP_3DO1");
                else
                {
                    NPCPhrase("YOUR_FLAGSHIP_PC");
                    NPCPhrase(Talk.GlobalShipName);
                }
                NPCPhrase("STARBASE_IS_READY_B");
                if (T.UsingSpeech)
                    NPCPhrase("YOUR_FLAGSHIP_3DO0");
                else
                    NPCPhrase(Talk.GlobalShipName);
                NPCPhrase("STARBASE_IS_READY_C");
                T.DeltaSISGauges(0, 0, 2500);
                SetGameState("STARBASE_MONTH", T.Clock.Month);
                SetGameState("STARBASE_DAY", T.Clock.Day);
            }
            else if (GetGameState("STARBASE_VISITED") != 0)
                CheckBulletins(false);
            else
            {
                char letter = "ABCDEFGH"[(byte)T.Random() & 7];
                NPCPhrase($"NORMAL_HELLO_{letter}0");
                if (!T.UsingSpeech)
                {
                    NPCPhrase("SPACE");
                    NPCPhrase(Talk.GlobalPlayerName);
                }
                NPCPhrase($"NORMAL_HELLO_{letter}1");
                CheckBulletins(false);
            }

            SetGameState("STARBASE_VISITED", 1);
        }

        if (Sis.Minerals.Sum() != 0)
            Response("have_minerals", SellMinerals);
        if (DiscussDevices(false))
            Response("new_devices", NormalStarbase);
        if (_curBulletinMask != 0)
            Response("repeat_bulletins", NormalStarbase);
        Response("need_info", NeedInfo);
        Response("goodbye_commander", ByeBye);
    }

    private void SellMinerals(int r)
    {
        // COUNT: what the load is worth, in 16 bits.
        ushort total = 0;
        var worth = T.Story.ElementWorth;
        for (int i = 0; i < Sis.Minerals.Count; ++i)
        {
            int amount = Sis.Minerals[i];
            if (amount == 0)
                continue;
            total = (ushort)(total + amount * worth[i]);
            Sis.Minerals[i] = 0;
            T.DeltaSISGauges(0, 0, amount * worth[i]);
        }

        string load = total < 1000 ? "LIGHT" : total < 2500 ? "MEDIUM" : "HEAVY";
        string counter = load + "_MINERAL_LOAD";
        int sold = GetGameState(counter);
        // The lines for the first six loads, then the last again and again.
        string? letter = sold <= 6 ? "ABCDEFG"[sold].ToString() : null;
        SetGameState(counter, sold == 6 ? 6 : sold + 1);

        if (letter is not null)
            NPCPhrase($"{load}_LOAD_{letter}0");
        if (!T.UsingSpeech)
        {
            NPCPhrase("SPACE");
            NPCPhrase(Talk.GlobalPlayerName);
        }
        if (letter is not null)
            NPCPhrase($"{load}_LOAD_{letter}1");

        NormalStarbase(r);
    }

    protected internal override void Intro() => NormalStarbase(0);

    protected internal override void PostEncounter()
    {
        SetGameState("MOONBASE_ON_SHIP", 0);
        if (GetGameState("CHMMR_BOMB_STATE") == 2)
            SetGameState("CHMMR_BOMB_STATE", 3);
    }

    protected internal override void Init()
    {
        // AlienTextWidth and AlienTextBaseline: in the description already.
        // The alternate starbase track, if there is one.
        T.AltSong = "comm.starbase.music";
        T.UseAltSong = true;

        _curBulletinMask = 0;
        T.SetSegue(Segue.Peace);
    }
}
