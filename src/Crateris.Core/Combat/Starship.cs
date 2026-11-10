// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/races.h: STARSHIP,
// RACE_DESC, SHIP_INFO, CHARACTERISTIC_STUFF, the status flags),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Combat;

/// <summary>STATUS_FLAGS (races.h): a ship's keys this frame and its state in the heat of battle.</summary>
[Flags]
public enum StatusFlags : ushort
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Thrust = 1 << 2,
    Weapon = 1 << 3,
    Special = 1 << 4,
    LowOnEnergy = 1 << 5,
    ShipBeyondMaxSpeed = 1 << 6,
    ShipAtMaxSpeed = 1 << 7,
    ShipInGravityWell = 1 << 8,
    PlayVictoryDitty = 1 << 9,

    /// <summary>The keys: LEFT | RIGHT | THRUST | WEAPON | SPECIAL.</summary>
    Keys = Left | Right | Thrust | Weapon | Special,
}

/// <summary>BATTLE_INPUT_STATE (controls.h): a side's keys for a frame.</summary>
[Flags]
public enum BattleInput : byte
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Thrust = 1 << 2,
    Weapon = 1 << 3,
    Special = 1 << 4,
    /// <summary>Retreat (BATTLE_ESCAPE), where allowed.</summary>
    Escape = 1 << 5,
}

/// <summary>CHARACTERISTIC_STUFF, as a ship flies with it (the flagship's comes from its modules).</summary>
public sealed class Characteristics
{
    public ushort MaxThrust, ThrustIncrement;
    public byte EnergyRegeneration, WeaponEnergyCost, SpecialEnergyCost, EnergyWait, TurnWait, ThrustWait,
        WeaponWait, SpecialWait, ShipMass;

    /// <summary>A ship's description's (data/ships.json).</summary>
    public static Characteristics From(ShipCharacteristics c) => new()
    {
        MaxThrust = (ushort)c.MaxThrust, ThrustIncrement = (ushort)c.ThrustIncrement,
        EnergyRegeneration = (byte)c.EnergyRegeneration, WeaponEnergyCost = (byte)c.WeaponEnergyCost,
        SpecialEnergyCost = (byte)c.SpecialEnergyCost, EnergyWait = (byte)c.EnergyWait, TurnWait = (byte)c.TurnWait,
        ThrustWait = (byte)c.ThrustWait, WeaponWait = (byte)c.WeaponWait, SpecialWait = (byte)c.SpecialWait,
        ShipMass = (byte)c.ShipMass,
    };
}

/// <summary>
/// A ship's RACE_DESC as a battle uses it: its SHIP_INFO (flags, crew and
/// energy now and at most), its characteristics, its sheets, and its own
/// callbacks (the ship's file's: before and after moving, and making its
/// weapon's elements). The battle's own ship code (ship.c) calls them.
/// </summary>
public sealed class RaceDesc
{
    public ShipFlags ShipFlags;
    public ushort CrewLevel, MaxCrew;
    public byte EnergyLevel, MaxEnergy;
    public Characteristics Characteristics = new();

    /// <summary>ship_data.ship and .weapon: the sheets at three sizes (null where the ship has none).</summary>
    public Views? Ship, Weapon;

    /// <summary>
    /// ship_data.special: the special's sheets at three sizes, null where the
    /// ship has none (the flagship's and the Ilwrath's: their specials,
    /// point defence's lasers and the cloak, draw no sheets). The AI tells a
    /// ship's special shots from its weapon's by this instance (cyborg.c
    /// compares an element's farray with the RACE_DESC's special array, for
    /// SEEKING_SPECIAL ships): a ship's special elements must take their
    /// <see cref="ElementState.FrameArray"/> from here, the same
    /// <see cref="Views"/>, not one made again from the same sheets
    /// (<see cref="BattleSheets.Views"/> gives the same instance for the
    /// same sheets). UQM's array is never NULL, so a ship of 4b whose special
    /// elements take it without sheets of their own needs one here all the
    /// same.
    /// </summary>
    public Views? Special;

    /// <summary>preprocess_func and postprocess_func: the ship's own steps, before ship.c's.</summary>
    public ElementProcess? Preprocess, Postprocess;

    /// <summary>
    /// init_weapon_func: the weapon's elements, made but not yet in the list
    /// (ship_postprocess puts them there), up to 6; null where one couldn't
    /// be made (the list full).
    /// </summary>
    public Func<Element, IReadOnlyList<Element?>>? InitWeapon;

    /// <summary>
    /// cyborg_control (INTEL_STUFF): how manoeuvrable the AI thinks the ship
    /// is (ManeuverabilityIndex: 0 until the AI first looks at the ship,
    /// when InitCyborg works it out from the characteristics) and how far
    /// its weapon reaches (WeaponRange), which the AI reads for this ship
    /// and its enemy.
    /// </summary>
    public ushort ManeuverabilityIndex, WeaponRange;

    /// <summary>
    /// cyborg_control.intelligence_func: the ship's own AI, given the
    /// ship (a copy of its element) and what tactical_intelligence found
    /// around it; null for a ship the AI can't fly.
    /// </summary>
    public IntelligenceFunc? Intelligence;

    /// <summary>
    /// uninit_func: called as the description is freed (free_ship), when
    /// the ship's element is done with in the battle: the flagship's
    /// (uninit_sis) gives back the crew it has left.
    /// </summary>
    public Action<RaceDesc>? Uninit;
}

/// <summary>Makes a ship's RACE_DESC for a battle (load_ship), the ship's own code bound to it.</summary>
public interface IShipCode
{
    RaceDesc Load(Battle battle, Starship starship);
}

/// <summary>
/// A ship in a side's fleet (STARSHIP): which ship, its crew between
/// battles, and in battle its RACE_DESC, its element, facing, keys and
/// counters.
/// </summary>
public sealed class Starship
{
    public Starship(IShipCode code, short playerNr, ushort crewLevel, ushort maxCrew)
    {
        Code = code;
        PlayerNr = playerNr;
        CrewLevel = crewLevel;
        MaxCrew = maxCrew;
    }

    /// <summary>The ship's code (UQM's SpeciesID).</summary>
    public IShipCode Code { get; }

    /// <summary>Whether it can still be picked to fight (SpeciesID isn't NO_ID).</summary>
    public bool Available = true;

    /// <summary>captains_name_index: 0 for the flagship.</summary>
    public byte CaptainsNameIndex;

    /// <summary>Its description while in battle (RaceDescPtr); null before and after.</summary>
    public RaceDesc? RaceDesc;

    /// <summary>The crew it goes into battle with, and has left after (crew_level); 0 for the flagship's own.</summary>
    public ushort CrewLevel;
    public ushort MaxCrew;
    public byte WeaponCounter, SpecialCounter, EnergyCounter;
    public StatusFlags ShipInputState, CurStatusFlags, OldStatusFlags;

    /// <summary>
    /// control's rating, as the AI last flew the ship (UQM sets control
    /// from PlayerControl each frame; the AI reads only its rating).
    /// </summary>
    public CyborgRating Rating = CyborgRating.Awesome;

    /// <summary>Its element in the battle (hShip), or none.</summary>
    public Element? Ship;
    public ushort ShipFacing;

    /// <summary>Its side: 0 the player's (RPG_PLAYER_NUM), 1 the enemy's (NPC_PLAYER_NUM).</summary>
    public short PlayerNr { get; }
}
