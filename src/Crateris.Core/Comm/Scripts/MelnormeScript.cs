// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/comm/melnorm/melnorm.c),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Data;
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Core.Comm.Scripts;

/// <summary>
/// The Melnorme (melnorm.c), whole: the first meeting and its small talk,
/// the bluster; the trading (fuel by the answering ship's own tanks, the
/// technology catalog, information with its story flags), selling bio
/// data and rainbow worlds; the anger, its battles and the strip that
/// calms it (ok_strip_me); the rescue of a ship out of fuel, with the
/// strip's own seeded generator (StripShip). Every path is checked against
/// UQM's own script (tests/golden/talks-melnorm.json).
/// <para>
/// The co-op game's readings (the stage 3b-2 plan's item 4 and its
/// questions 5 to 7): the talk reads and writes the answerer's own
/// credits live (<see cref="ITalkShip.Credits"/>), keeping
/// MELNORME_CREDIT0/1 in step as its events; the ship's worth, its tanks
/// and the strip are its own slots' (<see cref="ITalkShip.OwnModuleSlots"/>,
/// a guest's escort's with its kit crew); the catalog offers the first
/// item the answering ship lacks (a module anyone bought, a lander upgrade
/// this ship has); the two rescue fields are the answerer's own; the
/// rescue's take_it applies the strip as a change (the parts taken, the
/// fuel given up to the tanks' room), not UQM's restore of the whole ship
/// saved as the offer was made, so gifts that reached the ship meanwhile
/// stay. <see cref="RescueOnly"/> starts the talk at the rescue, without
/// the greeting (question 7's rescue-only talk).
/// </para>
/// </summary>
public sealed class MelnormeScript : CommScript
{
    public MelnormeScript(ConversationScript phrases) : base(phrases)
    {
    }

    /// <summary>
    /// The rescue-only talk (the owner's question 7): set before the talk
    /// starts, its intro goes straight to the rescue branch of
    /// NatureOfConversation (the rescue lines or "changed your mind?", then
    /// the strip offer), without Intro's greeting and NatureOfConversation's
    /// hello line and its write of the shared MELNORME_YACK_STACK2.
    /// </summary>
    public bool RescueOnly { get; set; }

    // The number speech (melnorme_numbers_english).
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

    private static readonly NumberSpeech MelnormeNumbersEnglish = new(new SpeechDigit[]
    {
        new(1000, 0, null, null, "ENUMERATE_THOUSAND"),
        new(100, 0, DigitNames, null, "ENUMERATE_HUNDRED"),
        new(10, 0, TensNames, null, null),
        new(1, 10, TeenNames, null, null),
        new(1, 0, DigitNames, null, null),
    });

    protected internal override NumberSpeech NumberSpeech => MelnormeNumbersEnglish;

    // sis.h's and the script's numbers.
    private const byte PlanetLander = ShipModules.PlanetLander, FusionThruster = ShipModules.FusionThruster,
        TurningJets = ShipModules.TurningJets, CrewPod = ShipModules.CrewPod, StorageBay = ShipModules.StorageBay,
        FuelTank = ShipModules.FuelTank, HighEffFuelSys = ShipModules.HighEfficiencyFuelSystem,
        BombModule0 = ShipModules.BombModule0, EmptySlot = ShipModules.EmptySlot;
    private const int CrewPodCapacity = 50, StorageBayCapacity = 500;
    private const uint FuelReserve = Hyperspace.Fuel.Reserve, FuelTankCapacity = Hyperspace.Fuel.TankSize,
        HeFuelTankCapacity = Hyperspace.Fuel.HighEfficiencyTankSize, FuelTankScale = Hyperspace.Fuel.Scale;
    private const int ModuleCostScale = 50, BioCreditValue = BioSale.CreditValue;
    private const int SolX = 1752, SolY = 1450;
    private const int TechPrice = 75 * BioCreditValue, InfoCost = 75;

    // ------------------------------------------------------------ the technology

    private enum TechType { Module, LanderShield, State }

    // tech_data_table: each technology's type, its module, disaster or upgrade, and a module's cost.
    private static readonly (TechType Type, int Subtype, int Info)[] TechData =
    {
        (TechType.Module, ShipModules.BlasterWeapon, 4000),
        (TechType.State, (int)LanderUpgrades.Speed, 1),
        (TechType.Module, ShipModules.AntimissileDefense, 4000),
        (TechType.LanderShield, (int)LanderUpgrades.BioShield, -1),
        (TechType.State, (int)LanderUpgrades.Cargo, 1),
        (TechType.Module, ShipModules.HighEfficiencyFuelSystem, 1000),
        (TechType.State, (int)LanderUpgrades.Shot, 1),
        (TechType.LanderShield, (int)LanderUpgrades.QuakeShield, -1),
        (TechType.Module, ShipModules.TrackingSystem, 5000),
        (TechType.LanderShield, (int)LanderUpgrades.LightningShield, -1),
        (TechType.LanderShield, (int)LanderUpgrades.LavaShield, -1),
        (TechType.Module, ShipModules.CannonWeapon, 6000),
        (TechType.Module, ShipModules.ShivaFurnace, 4000),
    };

    // The game-state field each lander upgrade is (each ship's own).
    private static string UpgradeField(LanderUpgrades u) => u switch
    {
        LanderUpgrades.Speed => "IMPROVED_LANDER_SPEED",
        LanderUpgrades.Cargo => "IMPROVED_LANDER_CARGO",
        LanderUpgrades.Shot => "IMPROVED_LANDER_SHOT",
        _ => "LANDER_SHIELDS",
    };

    // HasTech: a module anyone has bought (its shared price set), a lander upgrade this ship has.
    private bool HasTech(int tech)
    {
        var (type, subtype, _) = TechData[tech];
        return type switch
        {
            TechType.Module => T.ModuleCost(subtype) != 0,
            _ => (Sis.Upgrades & (LanderUpgrades)subtype) != 0,
        };
    }

    // GrantTech: the module's price set (on sale for everyone), or the ship's lander upgrade.
    private void GrantTech(int tech)
    {
        var (type, subtype, info) = TechData[tech];
        switch (type)
        {
            case TechType.Module:
                T.SetModuleCost(subtype, info / ModuleCostScale);
                return;
            case TechType.LanderShield:
            {
                Sis.Upgrades |= (LanderUpgrades)subtype;
                int shields = (int)(Sis.Upgrades & (LanderUpgrades.BioShield | LanderUpgrades.QuakeShield
                                                    | LanderUpgrades.LightningShield | LanderUpgrades.LavaShield));
                SetGameState("LANDER_SHIELDS", shields);
                return;
            }
            case TechType.State:
                Sis.Upgrades |= (LanderUpgrades)subtype;
                SetGameState(UpgradeField((LanderUpgrades)subtype), info);
                return;
        }
    }

    // tech_sale_catalog: in TechData's order, each NEW_TECH_n and OK_BUY_NEW_TECH_n.
    private int? GetNextTechForSale()
    {
        for (int i = 0; i < TechData.Length; ++i)
            if (!HasTech(i))
                return i;
        return null;
    }

    // ------------------------------------------------------------ the sales lines

    private static readonly string[] OkBuyEventLines = Enumerable.Range(1, 8).Select(i => $"OK_BUY_EVENT_{i}").ToArray();
    private static readonly string[] OkBuyAlienRaceLines = Enumerable.Range(1, 16).Select(i => $"OK_BUY_ALIEN_RACE_{i}").ToArray();
    private static readonly string[] OkBuyHistoryLines = Enumerable.Range(1, 9).Select(i => $"OK_BUY_HISTORY_{i}").ToArray();
    private static readonly string[] HelloAndDownToBusinessLines =
        Enumerable.Range(1, 10).Select(i => $"HELLO_AND_DOWN_TO_BUSINESS_{i}").ToArray();
    private static readonly string[] RescueLines =
        { "RESCUE_EXPLANATION", "RESCUE_AGAIN_1", "RESCUE_AGAIN_2", "RESCUE_AGAIN_3", "RESCUE_AGAIN_4", "RESCUE_AGAIN_5" };

    // GetLineSafe: the last line past the end.
    private static string GetLineSafe(string[] lines, int line) => lines[Math.Min(line, lines.Length - 1)];

    // ------------------------------------------------------------ the ship

    // The crew pods the crew aboard needs (the kit's crew held by the kit: a guest's escort's).
    private int CrewPodsNeeded() => (Math.Max(0, Sis.Crew - Sis.KitCrew) + CrewPodCapacity - 1) / CrewPodCapacity;

    // ShipWorth: the answering ship's landers, thrusters, jets and modules, at their prices, but the crew pods the crew needs.
    private int ShipWorth()
    {
        int worth = Sis.Landers * T.ModuleCost(PlanetLander);
        foreach (byte d in Sis.OwnDriveSlots)
            if (d < EmptySlot)
                worth += T.ModuleCost(FusionThruster);
        foreach (byte j in Sis.OwnJetSlots)
            if (j < EmptySlot)
                worth += T.ModuleCost(TurningJets);
        int crewPods = -CrewPodsNeeded();
        foreach (byte m in Sis.OwnModuleSlots)
            if (m < BombModule0 && (m != CrewPod || ++crewPods > 0))
                worth += T.ModuleCost(m);
        return (ushort)worth;
    }

    // The fuel the answering ship's tanks hold (DoBuy's and StripShip's own count, the slots from the last).
    private static uint Capacity(IList<byte> modules)
    {
        uint capacity = FuelReserve;
        for (int slot = modules.Count - 1; slot >= 0; --slot)
            if (modules[slot] is FuelTank or HighEffFuelSys)
                capacity += modules[slot] == FuelTank ? FuelTankCapacity : HeFuelTankCapacity;
        return capacity;
    }

    // The strip offered (StripShip's SIS_copy and rescue_fuel): what take_it takes, by slot, and the fuel it gives.
    private sealed record Strip(int Landers, List<int> Drives, List<int> Jets, List<int> Modules, uint Fuel);

    private Strip? _strip;

    /// <summary>
    /// Whether the rescue's strip is offered (take_it or leave_it to
    /// choose): the strip may take storage bays the minerals then aboard
    /// leave spare, so no minerals may be given to the ship meanwhile (the
    /// stage 3b-2 plan's item 4: <see cref="World.TradeRefusal.StripOffered"/>).
    /// </summary>
    public bool StripOffered => _strip is not null && !T.Ended;

    // GetStripModuleRef.
    private static string StripModuleRef(int module) => module switch
    {
        PlanetLander => "LANDERS",
        FusionThruster => "THRUSTERS",
        TurningJets => "JETS",
        CrewPod => "PODS",
        StorageBay => "BAYS",
        ShipModules.DynamoUnit => "DYNAMOS",
        ShipModules.ShivaFurnace => "FURNACES",
        ShipModules.GunWeapon => "GUNS",
        ShipModules.BlasterWeapon => "BLASTERS",
        ShipModules.CannonWeapon => "CANNONS",
        ShipModules.TrackingSystem => "TRACKERS",
        ShipModules.AntimissileDefense => "DEFENSES",
        _ => throw new InvalidOperationException($"the Melnorme don't strip module {module}"),
    };

    // getStripRandomSeed: where the ship is (truncated), its landers and four of its module slots.
    private uint StripRandomSeed()
    {
        var (lx, ly) = Sis.LogPlace;
        uint x = (uint)Units.LogXToUniverse(lx) / 100;
        uint y = (uint)Units.LogYToUniverse(ly) / 100;
        var m = Sis.OwnModuleSlots;
        return unchecked(y * 1013 + x + (uint)Sis.Landers + m[1] + m[4] + m[7] + m[10]);
    }

    // StripShip (fuel_required): the rescue's strip offered, worked out on a copy of the ship with the
    // strip's own generator, seeded from where the ship is and what's on it; true if offered (else charity).
    private bool StripForRescue(uint fuelRequired)
    {
        SetRescueRefused(0);
        var random = new TfbRandom(StripRandomSeed());   // RandomContext_SeedRandom: TFB_SeedRandom's coercion
        int landers = Sis.Landers;
        var drives = Sis.OwnDriveSlots.ToArray();
        var jets = Sis.OwnJetSlots.ToArray();
        var modules = Sis.OwnModuleSlots.ToArray();
        var moduleCount = new int[BombModule0];
        var strip = new Strip(0, new(), new(), new(), 0);

        uint capacity = Capacity(modules);
        if (fuelRequired > capacity)
            fuelRequired = capacity;

        int crewPods = -CrewPodsNeeded();
        int mass = Sis.Minerals.Sum();
        int bays = -((mass + StorageBayCapacity - 1) / StorageBayCapacity);
        foreach (byte m in modules)
        {
            if (m == CrewPod)
                ++crewPods;
            else if (m == StorageBay)
                ++bays;
        }

        ushort worth = (ushort)(fuelRequired / FuelTankScale), total = 0;
        byte numSearches = 0;
        int begMod = 0xFF, endMod = 0xFF;
        // The real ship's worth inside the copy's loop (UQM's quirk): it never changes, so with nothing
        // left on the copy to take, the searches end only as their byte count wraps.
        while (total < worth && ShipWorth() != 0 && ++numSearches != 0)
        {
            uint randVal = random.Next();
            int which = (int)(randVal & 0xFF) % (CrewPod + 1);
            int i;
            switch (which)
            {
                case PlanetLander:
                    if (landers == 0)
                        continue;
                    --landers;
                    strip = strip with { Landers = strip.Landers + 1 };
                    break;
                case FusionThruster:
                    for (i = 0; i < drives.Length; ++i)
                        if (drives[i] < EmptySlot)
                            break;
                    if (i == drives.Length)
                        continue;
                    drives[i] = EmptySlot + 0;
                    strip.Drives.Add(i);
                    break;
                case TurningJets:
                    for (i = 0; i < jets.Length; ++i)
                        if (jets[i] < EmptySlot)
                            break;
                    if (i == jets.Length)
                        continue;
                    jets[i] = EmptySlot + 1;
                    strip.Jets.Add(i);
                    break;
                case CrewPod:
                    i = (int)((randVal >> 8) & 0xFF) % modules.Length;
                    which = modules[i];
                    if (which >= BombModule0 || which == FuelTank || which == HighEffFuelSys
                        || (which == StorageBay && moduleCount[StorageBay] >= bays)
                        || (which == CrewPod && moduleCount[CrewPod] >= crewPods))
                        continue;
                    modules[i] = EmptySlot + 2;
                    strip.Modules.Add(i);
                    break;
            }

            if (begMod == 0xFF)
                begMod = endMod = which;
            else if (which > endMod)
                endMod = which;
            ++moduleCount[which];
            total = (ushort)(total + T.ModuleCost(which));
        }

        if (total == 0)
        {
            NPCPhrase("CHARITY");
            T.DeltaSISGauges(0, (int)fuelRequired, 0);
            return false;
        }
        NPCPhrase("RESCUE_OFFER");
        _strip = strip with { Fuel = fuelRequired };
        NPCPhrase(fuelRequired == capacity ? "RESCUE_TANKS" : "RESCUE_HOME");
        for (int m = PlanetLander; m < BombModule0; ++m)
        {
            if (moduleCount[m] == 0)
                continue;
            if (m == endMod && m != begMod)
                NPCPhrase("END_LIST_WITH_AND");
            NPCPhrase(T.Phrase("ENUMERATE_ONE") + (moduleCount[m] - 1));
            NPCPhrase(StripModuleRef(m));
        }
        return true;
    }

    // StripShip (0): the strip taken. UQM restores the whole ship as it was copied (the offer's) and adds
    // the fuel; here the strip is applied as a change (the stage 3b-2 plan's item 4): the landers and slots
    // it took, then the fuel up to the tanks' room, so gifts that reached the ship meanwhile stay.
    private void TakeStrip()
    {
        SetRescueRefused(0);
        var strip = _strip ?? throw new InvalidOperationException("no strip was offered");
        _strip = null;
        Sis.Landers = Math.Max(0, Sis.Landers - strip.Landers);
        foreach (int d in strip.Drives)
            Sis.OwnDriveSlots[d] = EmptySlot + 0;
        foreach (int j in strip.Jets)
            Sis.OwnJetSlots[j] = EmptySlot + 1;
        foreach (int m in strip.Modules)
            Sis.OwnModuleSlots[m] = EmptySlot + 2;
        T.DeltaSISGauges(Talk.UndefinedDelta, (int)strip.Fuel, Talk.UndefinedDelta);
    }

    // StripShip ((COUNT)~0): the whole ship stripped to calm their anger (ok_strip_me), but its fuel
    // tanks' reserve, the crew pods its crew needs and the bomb's parts.
    private void StripWhole()
    {
        SetRescueRefused(0);
        int crewPods = -CrewPodsNeeded();
        Sis.Landers = 0;
        for (int i = 0; i < Sis.OwnDriveSlots.Count; ++i)
            Sis.OwnDriveSlots[i] = EmptySlot + 0;
        for (int i = 0; i < Sis.OwnJetSlots.Count; ++i)
            Sis.OwnJetSlots[i] = EmptySlot + 1;
        if (Sis.Fuel > FuelReserve)
            Sis.Fuel = FuelReserve;
        Sis.BioData = 0;
        for (int i = 0; i < Sis.Minerals.Count; ++i)
            Sis.Minerals[i] = 0;
        for (int i = 0; i < Sis.OwnModuleSlots.Count; ++i)
        {
            byte which = Sis.OwnModuleSlots[i];
            if (which < BombModule0 && (which != CrewPod || ++crewPods > 0))
                Sis.OwnModuleSlots[i] = EmptySlot + 2;
        }
        T.DeltaSISGauges(Talk.UndefinedDelta, Talk.UndefinedDelta, Talk.UndefinedDelta);
    }

    // MELNORME_RESCUE_REFUSED and MELNORME_RESCUE_COUNT: the answerer's own (question 7).
    private void SetRescueRefused(int value)
    {
        Sis.RescueRefused = value;
        SetGameState("MELNORME_RESCUE_REFUSED", value);
    }

    private void SetRescueCount(int value)
    {
        Sis.RescueCount = value;
        SetGameState("MELNORME_RESCUE_COUNT", value);
    }

    // ------------------------------------------------------------ the talk

    private void ExitConversation(int r)
    {
        if (PlayerSaid(r, "no_trade_now"))
            NPCPhrase("OK_NO_TRADE_NOW_BYE");
        else if (PlayerSaid(r, "youre_on"))
        {
            NPCPhrase("YOU_GIVE_US_NO_CHOICE");

            SetGameState("MELNORME_ANGER", 1);
            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "so_we_can_attack"))
        {
            NPCPhrase("DECEITFUL_HUMAN");

            SetGameState("MELNORME_ANGER", 2);
            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "bye_melnorme_slightly_angry"))
            NPCPhrase("MELNORME_SLIGHTLY_ANGRY_GOODBYE");
        else if (PlayerSaid(r, "ok_strip_me"))
        {
            if (ShipWorth() < 4000 / ModuleCostScale)
                NPCPhrase("NOT_WORTH_STRIPPING");
            else
            {
                SetGameState("MELNORME_ANGER", 0);

                StripWhole();
                NPCPhrase("FAIR_JUSTICE");
            }
        }
        else if (PlayerSaid(r, "fight_some_more"))
        {
            NPCPhrase("OK_FIGHT_SOME_MORE");

            SetGameState("MELNORME_ANGER", 3);
            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "bye_melnorme_pissed_off"))
            NPCPhrase("MELNORME_PISSED_OFF_GOODBYE");
        else if (PlayerSaid(r, "well_if_thats_the_way_you_feel"))
        {
            NPCPhrase("WE_FIGHT_AGAIN");

            T.SetSegue(Segue.Hostile);
        }
        else if (PlayerSaid(r, "you_hate_us_so_we_go_away"))
            NPCPhrase("HATE_YOU_GOODBYE");
        else if (PlayerSaid(r, "take_it"))
        {
            TakeStrip();
            NPCPhrase("HAPPY_TO_HAVE_RESCUED");
        }
        else if (PlayerSaid(r, "leave_it"))
        {
            _strip = null;
            SetRescueRefused(1);
            NPCPhrase("MAYBE_SEE_YOU_LATER");
        }
        else if (PlayerSaid(r, "no_help"))
        {
            SetRescueRefused(1);
            NPCPhrase("GOODBYE_AND_GOODLUCK");
        }
        else if (PlayerSaid(r, "no_changed_mind"))
            NPCPhrase("GOODBYE_AND_GOODLUCK_AGAIN");
        else if (PlayerSaid(r, "be_leaving_now") || PlayerSaid(r, "goodbye"))
            NPCPhrase("FRIENDLY_GOODBYE");
    }

    private void DoRescue(int r)
    {
        // Measured to Sol from the answering ship (its own place on the star map).
        var (lx, ly) = Sis.LogPlace;
        int dx = Units.LogXToUniverse(lx) - SolX;
        int dy = Units.LogYToUniverse(ly) - SolY;
        uint fuelRequired = (uint)(ushort)(UqmMath.SquareRoot((uint)((long)dx * dx + (long)dy * dy)) + 2 * FuelTankScale);

        if (StripForRescue(fuelRequired))
        {
            Response("take_it", ExitConversation);
            Response("leave_it", ExitConversation);
        }
    }

    // GetAvailableCredits: the answerer's own, live.
    private uint GetAvailableCredits() => Sis.Credits;

    // SetAvailableCredits: the answerer's, and MELNORME_CREDIT0/1 as the script writes them.
    private void SetAvailableCredits(uint credits)
    {
        Sis.Credits = credits;
        SetGameState("MELNORME_CREDIT0", (byte)credits);
        SetGameState("MELNORME_CREDIT1", (byte)(credits >> 8));
    }

    // DeltaCredit: whether the purchase went through (no 16-bit wrap: docs/differences.md row 19).
    private bool DeltaCredit(long delta)
    {
        uint credit = GetAvailableCredits();
        if (delta >= 0 || -delta <= credit)
        {
            SetAvailableCredits((uint)Math.Clamp(credit + delta, 0, uint.MaxValue));
            return true;
        }
        NPCPhrase("NEED_MORE_CREDIT0");
        T.NPCNumber((int)(-delta - credit));
        NPCPhrase("NEED_MORE_CREDIT1");
        return false;
    }

    private void CurrentEvents()
    {
        int stack = GetGameState("MELNORME_EVENTS_INFO_STACK");
        NPCPhrase(GetLineSafe(OkBuyEventLines, stack));
        SetGameState("MELNORME_EVENTS_INFO_STACK", stack + 1);
    }

    private void AlienRaces()
    {
        int stack = GetGameState("MELNORME_ALIEN_INFO_STACK");
        string phrase = GetLineSafe(OkBuyAlienRaceLines, stack);
        // Two pieces of alien knowledge change the story.
        switch (phrase)
        {
            case "OK_BUY_ALIEN_RACE_14":
                if (GetGameState("FOUND_PLUTO_SPATHI") == 0)
                {
                    SetGameState("KNOW_SPATHI_PASSWORD", 1);
                    SetGameState("SPATHI_HOME_VISITS", 7);
                }
                break;
            case "OK_BUY_ALIEN_RACE_15":
                if (GetGameState("KNOW_ABOUT_SHATTERED") < 2)
                    SetGameState("KNOW_ABOUT_SHATTERED", 2);
                SetGameState("KNOW_SYREEN_WORLD_SHATTERED", 1);
                break;
        }
        NPCPhrase(phrase);
        SetGameState("MELNORME_ALIEN_INFO_STACK", stack + 1);
    }

    private void History()
    {
        int stack = GetGameState("MELNORME_HISTORY_INFO_STACK");
        NPCPhrase(GetLineSafe(OkBuyHistoryLines, stack));
        SetGameState("MELNORME_HISTORY_INFO_STACK", stack + 1);
    }

    private bool AnyInfoLeftToSell() =>
        GetGameState("MELNORME_EVENTS_INFO_STACK") < OkBuyEventLines.Length
        || GetGameState("MELNORME_ALIEN_INFO_STACK") < OkBuyAlienRaceLines.Length
        || GetGameState("MELNORME_HISTORY_INFO_STACK") < OkBuyHistoryLines.Length;

    private bool _askedToBuy;

    private void DoBuy(int r)
    {
        uint credit = GetAvailableCredits();
        // The answering ship's own tanks.
        uint capacity = Capacity(Sis.OwnModuleSlots);

        if (credit == 0)
        {
            // Out of credits: how commerce works.
            _askedToBuy = true;
            NPCPhrase("NEED_CREDIT");

            NatureOfConversation(r);
        }
        else if (PlayerSaid(r, "buy_fuel") || PlayerSaid(r, "buy_1_fuel") || PlayerSaid(r, "buy_5_fuel")
                 || PlayerSaid(r, "buy_10_fuel") || PlayerSaid(r, "buy_25_fuel") || PlayerSaid(r, "fill_me_up"))
        {
            long neededCredit = 0;
            if (PlayerSaid(r, "buy_1_fuel"))
                neededCredit = 1;
            else if (PlayerSaid(r, "buy_5_fuel"))
                neededCredit = 5;
            else if (PlayerSaid(r, "buy_10_fuel"))
                neededCredit = 10;
            else if (PlayerSaid(r, "buy_25_fuel"))
                neededCredit = 25;
            else if (PlayerSaid(r, "fill_me_up"))
                neededCredit = (short)((capacity - Sis.Fuel + FuelTankScale - 1) / FuelTankScale);

            if (neededCredit == 0)
            {
                if (GetGameState("MELNORME_FUEL_PROCEDURE") == 0)
                {
                    NPCPhrase("BUY_FUEL_INTRO");
                    SetGameState("MELNORME_FUEL_PROCEDURE", 1);
                }
            }
            else
            {
                if (Sis.Fuel / FuelTankScale + neededCredit > capacity / FuelTankScale)
                {
                    NPCPhrase("NO_ROOM_FOR_FUEL");
                    TryFuelAgain();
                    return;
                }

                if (neededCredit * (BioCreditValue / 2) <= credit)
                {
                    NPCPhrase("GOT_FUEL");

                    long f = neededCredit * FuelTankScale;
                    while (f > 0x3FFF)
                    {
                        T.DeltaSISGauges(0, 0x3FFF, 0);
                        f -= 0x3FFF;
                    }
                    T.DeltaSISGauges(0, (int)f, 0);
                }
                neededCredit *= BioCreditValue / 2;
            }
            if (neededCredit != 0)
            {
                DeltaCredit(-neededCredit);
                if (Sis.Fuel >= capacity)
                {
                    BuyBuyBuy(capacity);
                    return;
                }
            }
            TryFuelAgain();
        }
        else if (PlayerSaid(r, "buy_technology") || PlayerSaid(r, "buy_new_tech"))
        {
            // The catalog: the first item the answering ship lacks (the owner's question 5).
            if (GetGameState("MELNORME_TECH_PROCEDURE") == 0)
            {
                NPCPhrase("BUY_NEW_TECH_INTRO");
                SetGameState("MELNORME_TECH_PROCEDURE", 1);
            }

            int? nextTech;
            if (PlayerSaid(r, "buy_new_tech"))
            {
                nextTech = GetNextTechForSale();
                if (nextTech is not int bought || !DeltaCredit(-TechPrice))
                {
                    BuyBuyBuy(capacity);
                    return;
                }
                GrantTech(bought);
                NPCPhrase($"OK_BUY_NEW_TECH_{bought + 1}");
            }

            nextTech = GetNextTechForSale();
            if (nextTech is not int next)
            {
                NPCPhrase("NEW_TECH_ALL_GONE");
                BuyBuyBuy(capacity);
                return;
            }

            NPCPhrase($"NEW_TECH_{next + 1}");

            Response("buy_new_tech", DoBuy);
            Response("no_buy_new_tech", DoBuy);
        }
        else if (PlayerSaid(r, "buy_info") || PlayerSaid(r, "buy_current_events") || PlayerSaid(r, "buy_alien_races")
                 || PlayerSaid(r, "buy_history"))
        {
            if (GetGameState("MELNORME_INFO_PROCEDURE") == 0)
            {
                NPCPhrase("BUY_INFO_INTRO");
                SetGameState("MELNORME_INFO_PROCEDURE", 1);
            }
            else if (PlayerSaid(r, "buy_info"))
                NPCPhrase("OK_BUY_INFO");
            else
            {
                if (!DeltaCredit(-InfoCost))
                {
                    BuyBuyBuy(capacity);
                    return;
                }

                if (PlayerSaid(r, "buy_current_events"))
                    CurrentEvents();
                else if (PlayerSaid(r, "buy_alien_races"))
                    AlienRaces();
                else if (PlayerSaid(r, "buy_history"))
                    History();
            }

            if (!AnyInfoLeftToSell())
            {
                NPCPhrase("INFO_ALL_GONE");
                BuyBuyBuy(capacity);
                return;
            }

            if (GetGameState("MELNORME_EVENTS_INFO_STACK") < OkBuyEventLines.Length)
                Response("buy_current_events", DoBuy);
            if (GetGameState("MELNORME_ALIEN_INFO_STACK") < OkBuyAlienRaceLines.Length)
                Response("buy_alien_races", DoBuy);
            if (GetGameState("MELNORME_HISTORY_INFO_STACK") < OkBuyHistoryLines.Length)
                Response("buy_history", DoBuy);
            Response("done_buying_info", DoBuy);
        }
        else
        {
            if (PlayerSaid(r, "done_buying_fuel"))
                NPCPhrase("OK_DONE_BUYING_FUEL");
            else if (PlayerSaid(r, "no_buy_new_tech"))
                NPCPhrase("OK_NO_BUY_NEW_TECH");
            else if (PlayerSaid(r, "done_buying_info"))
                NPCPhrase("OK_DONE_BUYING_INFO");
            else
                NPCPhrase("WHAT_TO_BUY");

            BuyBuyBuy(capacity);
        }
    }

    // DoBuy's TryFuelAgain.
    private void TryFuelAgain()
    {
        NPCPhrase("HOW_MUCH_FUEL");

        Response("buy_1_fuel", DoBuy);
        Response("buy_5_fuel", DoBuy);
        Response("buy_10_fuel", DoBuy);
        Response("buy_25_fuel", DoBuy);
        Response("fill_me_up", DoBuy);
        Response("done_buying_fuel", DoBuy);
    }

    // DoBuy's BuyBuyBuy.
    private void BuyBuyBuy(uint capacity)
    {
        if (Sis.Fuel < capacity)
            Response("buy_fuel", DoBuy);
        if (GetNextTechForSale() is not null)
            Response("buy_technology", DoBuy);
        if (AnyInfoLeftToSell())
            Response("buy_info", DoBuy);

        Response("done_buying", NatureOfConversation);
        Response("be_leaving_now", ExitConversation);
    }

    // The rainbow worlds found and not yet sold: the bits of RAINBOW_WORLD0/1 less MELNORME_RAINBOW_COUNT (a byte).
    private byte NewRainbows()
    {
        int mask = GetGameState("RAINBOW_WORLD0") | (GetGameState("RAINBOW_WORLD1") << 8);
        byte n = (byte)-GetGameState("MELNORME_RAINBOW_COUNT");
        for (; mask != 0; mask >>= 1)
            if ((mask & 1) != 0)
                ++n;
        return n;
    }

    private void DoSell(int r)
    {
        byte numNewRainbows = NewRainbows();
        bool whatToSellQueued = false;

        if (!PlayerSaid(r, "sell"))
        {
            if (PlayerSaid(r, "sell_life_data"))
            {
                int bio = Sis.BioData;
                long addedCredit = (long)bio * BioCreditValue;

                NPCPhrase("SOLD_LIFE_DATA1");
                T.NPCNumber(bio);
                NPCPhrase("SOLD_LIFE_DATA2");
                T.NPCNumber((int)addedCredit);
                NPCPhrase("SOLD_LIFE_DATA3");
                // WHAT_TO_SELL queued before the talk segue.
                if (numNewRainbows != 0)
                {
                    NPCPhrase("WHAT_TO_SELL");
                    whatToSellQueued = true;
                }
                T.AlienTalkSegue(1);

                // The count-down on the cargo display, a unit at a time, a key selling the rest at once:
                // sold at once here, as the recording sells it (a key pressed at once); the seller's client
                // counts it down (BioSales).
                T.SoldBioData(new BioSale(Sis.Minerals.ToArray(), T.Story.ElementWorth.ToArray(), bio, Sis.StorageCapacity,
                    GetAvailableCredits()));
                DeltaCredit(addedCredit);
                Sis.BioData = 0;
            }
            else // sell_rainbow_locations
            {
                long addedCredit = numNewRainbows * (250 * BioCreditValue);

                NPCPhrase("SOLD_RAINBOW_LOCATIONS1");
                T.NPCNumber(numNewRainbows);
                NPCPhrase("SOLD_RAINBOW_LOCATIONS2");
                T.NPCNumber((int)addedCredit);
                NPCPhrase("SOLD_RAINBOW_LOCATIONS3");

                numNewRainbows = (byte)(numNewRainbows + GetGameState("MELNORME_RAINBOW_COUNT"));
                SetGameState("MELNORME_RAINBOW_COUNT", numNewRainbows);
                numNewRainbows = 0;

                DeltaCredit(addedCredit);
            }

            _askedToBuy = false;
        }

        if (Sis.BioData != 0 || numNewRainbows != 0)
        {
            if (!whatToSellQueued)
                NPCPhrase("WHAT_TO_SELL");

            if (Sis.BioData != 0)
                Response("sell_life_data", DoSell);
            if (numNewRainbows != 0)
                Response("sell_rainbow_locations", DoSell);
            Response("done_selling", NatureOfConversation);
        }
        else
        {
            if (PlayerSaid(r, "sell"))
                NPCPhrase("NOTHING_TO_SELL");
            DisablePhrase("sell");

            NatureOfConversation(r);
        }
    }

    private void NatureOfConversation(int r)
    {
        if (PlayerSaid(r, "get_on_with_business"))
        {
            SetGameState("MELNORME_YACK_STACK2", 5);
            r = 0;
        }

        // The credits drawn.
        DeltaCredit(0);
        uint credit = GetAvailableCredits();
        if (r == 0)
        {
            int stack = (byte)(GetGameState("MELNORME_YACK_STACK2") - 5);
            NPCPhrase(GetLineSafe(HelloAndDownToBusinessLines, stack));
            if (stack < HelloAndDownToBusinessLines.Length - 1)
                ++stack;
            SetGameState("MELNORME_YACK_STACK2", stack + 5);
        }

        byte numNewRainbows = NewRainbows();

        if (Sis.Fuel > 0 || Sis.BioData != 0 || credit != 0 || numNewRainbows != 0)
        {
            if (GetGameState("TRADED_WITH_MELNORME") == 0)
            {
                SetGameState("TRADED_WITH_MELNORME", 1);

                NPCPhrase("TRADING_INFO");
            }

            if (r == 0)
            {
                // The Melnorme report any news and turn purple.
                NPCPhrase("BUY_OR_SELL");
                T.AlienTalkSegue(1);
                T.XFormColorMap(1, Talk.OneSecond / 2);
                T.AlienTalkSegue(Talk.WaitAll);
            }
            else if (PlayerSaid(r, "why_turned_purple"))
            {
                SetGameState("WHY_MELNORME_PURPLE", 1);

                NPCPhrase("TURNED_PURPLE_BECAUSE");
            }
            else if (PlayerSaid(r, "done_selling"))
                NPCPhrase("OK_DONE_SELLING");
            else if (PlayerSaid(r, "done_buying"))
                NPCPhrase("OK_DONE_BUYING");

            if (GetGameState("WHY_MELNORME_PURPLE") == 0)
                Response("why_turned_purple", NatureOfConversation);
            if (!_askedToBuy)
                Response("buy", DoBuy);
            if (PhraseEnabled("sell"))
                Response("sell", DoSell);
            Response("goodbye", ExitConversation);
        }
        else
            NeedsRescue();
    }

    // NatureOfConversation's rescue branch: the ship has nothing to trade.
    private void NeedsRescue()
    {
        if (Sis.RescueRefused != 0)
        {
            NPCPhrase("CHANGED_MIND");

            Response("yes_changed_mind", DoRescue);
            Response("no_changed_mind", ExitConversation);
        }
        else
        {
            int numRescues = Sis.RescueCount;
            NPCPhrase(GetLineSafe(RescueLines, numRescues));

            if (numRescues < RescueLines.Length - 1)
                SetRescueCount(numRescues + 1);

            NPCPhrase("SHOULD_WE_HELP_YOU");

            Response("yes_help", DoRescue);
            Response("no_help", ExitConversation);
        }
    }

    // The script's own state, kept between the replies.
    private int _localStack0, _localStack1;

    private void DoBluster(int r)
    {
        if (PlayerSaid(r, "trade_is_for_the_weak"))
        {
            T.XFormColorMap(2, Talk.OneSecond / 2);

            SetGameState("MELNORME_YACK_STACK2", 4);
            NPCPhrase("WERE_NOT_AFRAID");
        }
        else if (PlayerSaid(r, "why_blue_light"))
        {
            SetGameState("WHY_MELNORME_BLUE", 1);

            NPCPhrase("BLUE_IS_MAD");
        }
        else if (PlayerSaid(r, "we_strong_1"))
        {
            _localStack0 = 1;
            NPCPhrase("YOU_NOT_STRONG_1");
        }
        else if (PlayerSaid(r, "we_strong_2"))
        {
            _localStack0 = 2;
            NPCPhrase("YOU_NOT_STRONG_2");
        }
        else if (PlayerSaid(r, "we_strong_3"))
        {
            _localStack0 = 3;
            NPCPhrase("YOU_NOT_STRONG_3");
        }
        else if (PlayerSaid(r, "just_testing"))
        {
            _localStack1 = 1;
            NPCPhrase("REALLY_TESTING");
        }

        if (GetGameState("WHY_MELNORME_BLUE") == 0)
            Response("why_blue_light", DoBluster);
        switch (_localStack0)
        {
            case 0:
                Response("we_strong_1", DoBluster);
                break;
            case 1:
                Response("we_strong_2", DoBluster);
                break;
            case 2:
                Response("we_strong_3", DoBluster);
                break;
        }
        switch (_localStack1)
        {
            case 0:
                Response("just_testing", DoBluster);
                break;
            case 1:
                Response("yes_really_testing", DoFirstMeeting);
                break;
        }
        Response("youre_on", ExitConversation);
    }

    private void Yack0Respond()
    {
        switch (GetGameState("MELNORME_YACK_STACK0"))
        {
            case 0:
                T.ConstructResponse("we_are_from_alliance0", T.GetAllianceName("name_1"));
                T.DoResponsePhrase("we_are_from_alliance0", DoFirstMeeting, Talk.SharedPhraseBuffer);
                break;
            case 1:
                Response("how_know", DoFirstMeeting);
                break;
        }
    }

    private void Yack1Respond()
    {
        switch (GetGameState("MELNORME_YACK_STACK1"))
        {
            case 0:
                Response("what_about_yourselves", DoFirstMeeting);
                break;
            case 1:
                Response("what_factors", DoFirstMeeting);
                Response("get_on_with_business", NatureOfConversation);   // UQM's case falls through
                break;
            case 2:
                Response("get_on_with_business", NatureOfConversation);
                break;
        }
    }

    private void Yack2Respond()
    {
        switch (GetGameState("MELNORME_YACK_STACK2"))
        {
            case 0:
                Response("what_about_universe", DoFirstMeeting);
                break;
            case 1:
                Response("giving_is_good_1", DoFirstMeeting);
                break;
            case 2:
                Response("giving_is_good_2", DoFirstMeeting);
                break;
            case 3:
                Response("trade_is_for_the_weak", DoBluster);
                break;
        }
    }

    private void DoFirstMeeting(int r)
    {
        int lastStack = 0;
        Action[] stackFunc = { Yack0Respond, Yack1Respond, Yack2Respond };

        if (r == 0)
        {
            int businessCount = GetGameState("MELNORME_BUSINESS_COUNT");
            switch (businessCount++)
            {
                case 0:
                    NPCPhrase("HELLO_NOW_DOWN_TO_BUSINESS_1");
                    break;
                case 1:
                    NPCPhrase("HELLO_NOW_DOWN_TO_BUSINESS_2");
                    break;
                case 2:
                    NPCPhrase("HELLO_NOW_DOWN_TO_BUSINESS_3");
                    --businessCount;
                    break;
            }
            SetGameState("MELNORME_BUSINESS_COUNT", businessCount);
        }
        else if (PlayerSaid(r, "we_are_from_alliance0"))
        {
            SetGameState("MELNORME_YACK_STACK0", 1);
            NPCPhrase("KNOW_OF_YOU");
        }
        else if (PlayerSaid(r, "how_know"))
        {
            SetGameState("MELNORME_YACK_STACK0", 2);
            NPCPhrase("KNOW_BECAUSE");
        }
        else if (PlayerSaid(r, "what_about_yourselves"))
        {
            lastStack = 1;
            SetGameState("MELNORME_YACK_STACK1", 1);
            NPCPhrase("NO_TALK_ABOUT_OURSELVES");
        }
        else if (PlayerSaid(r, "what_factors"))
        {
            lastStack = 1;
            SetGameState("MELNORME_YACK_STACK1", 2);
            NPCPhrase("FACTORS_ARE");
        }
        else if (PlayerSaid(r, "what_about_universe"))
        {
            lastStack = 2;
            SetGameState("MELNORME_YACK_STACK2", 1);
            NPCPhrase("NO_FREE_LUNCH");
        }
        else if (PlayerSaid(r, "giving_is_good_1"))
        {
            lastStack = 2;
            SetGameState("MELNORME_YACK_STACK2", 2);
            NPCPhrase("GIVING_IS_BAD_1");
        }
        else if (PlayerSaid(r, "giving_is_good_2"))
        {
            lastStack = 2;
            SetGameState("MELNORME_YACK_STACK2", 3);
            NPCPhrase("GIVING_IS_BAD_2");
        }
        else if (PlayerSaid(r, "yes_really_testing"))
        {
            T.XFormColorMap(0, Talk.OneSecond / 2);

            NPCPhrase("TEST_RESULTS");
        }
        else if (PlayerSaid(r, "we_apologize"))
        {
            SetGameState("MELNORME_ANGER", 0);
            T.XFormColorMap(0, Talk.OneSecond / 2);

            NPCPhrase("APOLOGY_ACCEPTED");
        }

        (stackFunc[0], stackFunc[lastStack]) = (stackFunc[lastStack], stackFunc[0]);
        stackFunc[0]();
        stackFunc[1]();
        stackFunc[2]();
        Response("no_trade_now", ExitConversation);
    }

    private void DoMelnormeMiffed(int r)
    {
        if (r == 0)
        {
            int miffedCount = GetGameState("MELNORME_MIFFED_COUNT");
            switch (miffedCount++)
            {
                case 0:
                    NPCPhrase("HELLO_SLIGHTLY_ANGRY_1");
                    break;
                case 1:
                    NPCPhrase("HELLO_SLIGHTLY_ANGRY_2");
                    break;
                default:
                    --miffedCount;
                    NPCPhrase("HELLO_SLIGHTLY_ANGRY_3");
                    break;
            }
            SetGameState("MELNORME_MIFFED_COUNT", miffedCount);

            T.XFormColorMap(2, Talk.OneSecond / 2);
        }
        else if (PlayerSaid(r, "explore_relationship"))
        {
            SetGameState("MELNORME_YACK_STACK3", 1);

            NPCPhrase("EXAMPLE_OF_RELATIONSHIP");
        }
        else if (PlayerSaid(r, "excuse_1"))
        {
            SetGameState("MELNORME_YACK_STACK3", 2);

            NPCPhrase("NO_EXCUSE_1");
        }
        else if (PlayerSaid(r, "excuse_2"))
        {
            SetGameState("MELNORME_YACK_STACK3", 3);

            NPCPhrase("NO_EXCUSE_2");
        }
        else if (PlayerSaid(r, "excuse_3"))
        {
            SetGameState("MELNORME_YACK_STACK3", 4);

            NPCPhrase("NO_EXCUSE_3");
        }

        switch (GetGameState("MELNORME_YACK_STACK3"))
        {
            case 0:
                Response("explore_relationship", DoMelnormeMiffed);
                break;
            case 1:
                Response("excuse_1", DoMelnormeMiffed);
                break;
            case 2:
                Response("excuse_2", DoMelnormeMiffed);
                break;
            case 3:
                Response("excuse_3", DoMelnormeMiffed);
                break;
        }
        Response("we_apologize", DoFirstMeeting);
        Response("so_we_can_attack", ExitConversation);
        Response("bye_melnorme_slightly_angry", ExitConversation);
    }

    private void DoMelnormePissed(int r)
    {
        if (r == 0)
        {
            int pissedCount = GetGameState("MELNORME_PISSED_COUNT");
            switch (pissedCount++)
            {
                case 0:
                    NPCPhrase("HELLO_PISSED_OFF_1");
                    break;
                case 1:
                    NPCPhrase("HELLO_PISSED_OFF_2");
                    break;
                default:
                    --pissedCount;
                    NPCPhrase("HELLO_PISSED_OFF_3");
                    break;
            }
            SetGameState("MELNORME_PISSED_COUNT", pissedCount);

            T.XFormColorMap(2, Talk.OneSecond / 2);
        }
        else if (PlayerSaid(r, "beg_forgiveness"))
        {
            SetGameState("MELNORME_YACK_STACK4", 1);

            NPCPhrase("LOTS_TO_MAKE_UP_FOR");
        }
        else if (PlayerSaid(r, "you_are_so_right"))
        {
            SetGameState("MELNORME_YACK_STACK4", 2);

            NPCPhrase("ONE_LAST_CHANCE");
        }

        switch (GetGameState("MELNORME_YACK_STACK4"))
        {
            case 0:
                Response("beg_forgiveness", DoMelnormePissed);
                break;
            case 1:
                Response("you_are_so_right", DoMelnormePissed);
                break;
            case 2:
                Response("ok_strip_me", ExitConversation);
                break;
        }
        Response("fight_some_more", ExitConversation);
        Response("bye_melnorme_pissed_off", ExitConversation);
    }

    private void DoMelnormeHate(int r)
    {
        int hateCount = GetGameState("MELNORME_HATE_COUNT");
        switch (hateCount++)
        {
            case 0:
                NPCPhrase("HELLO_HATE_YOU_1");
                break;
            case 1:
                NPCPhrase("HELLO_HATE_YOU_2");
                break;
            default:
                --hateCount;
                NPCPhrase("HELLO_HATE_YOU_3");
                break;
        }
        SetGameState("MELNORME_HATE_COUNT", hateCount);

        T.XFormColorMap(2, Talk.OneSecond / 2);

        Response("well_if_thats_the_way_you_feel", ExitConversation);
        Response("you_hate_us_so_we_go_away", ExitConversation);
    }

    protected internal override void Intro()
    {
        // SetStatusMessageMode (SMM_CREDITS): the status area shows the credits for the talk's time.
        T.ShowsCredits = true;

        if (RescueOnly)
        {
            // The rescue-only talk (question 7): NatureOfConversation's credits drawn, then its rescue
            // branch, without the greeting.
            DeltaCredit(0);
            NeedsRescue();
            return;
        }

        if (GetGameState("MET_MELNORME") == 0)
        {
            SetGameState("MET_MELNORME", 1);
            DoFirstMeeting(0);
        }
        else
        {
            switch (GetGameState("MELNORME_ANGER"))
            {
                case 0:
                    if (GetGameState("MELNORME_YACK_STACK2") <= 5)
                        DoFirstMeeting(0);
                    else
                        NatureOfConversation(0);
                    break;
                case 1:
                    DoMelnormeMiffed(0);
                    break;
                case 2:
                    DoMelnormePissed(0);
                    break;
                default:
                    DoMelnormeHate(0);
                    break;
            }
        }
    }

    // post_melnorme_enc: the status area's message as it was.
    protected internal override void PostEncounter() => T.ShowsCredits = false;

    protected internal override void Init()
    {
        // AlienTextBaseline and AlienTextWidth: in the description already.
        _localStack0 = 0;
        _localStack1 = 0;
        T.SetSegue(Segue.Peace);
        _askedToBuy = false;
    }
}
