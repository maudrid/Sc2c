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

    /// <summary>
    /// The armada it's one of (a player's ships picked one at a time on the
    /// pick screen, <see cref="Combat.Armada"/>), or null: a ship that comes
    /// in by itself (the enemy's, a player's only ship).
    /// </summary>
    public Armada? Armada { get; internal set; }

    /// <summary>
    /// The status panel's icons (ship_info.icons, data/ships.json's
    /// graphics.icons), whose frame 1, the ship's silhouette, the VUX's
    /// limpets are placed on (<see cref="Battle.ModifySilhouette"/>); null
    /// for a ship without them, on which a limpet places nothing and draws
    /// no random numbers, as UQM's. Whoever makes a ship for a battle sets
    /// them (<see cref="RaceShips.Icons"/>), as load_ship loads them.
    /// </summary>
    public Views? Icons;

    /// <summary>
    /// What ModifySilhouette has done to the ship's captain's window since
    /// it was last drawn (DrawCaptainsWindow, as the ship warps in or is
    /// reborn), in order: the Orz's marines aboard and the VUX's limpets
    /// stuck on it. Kept for the client (stage 4b item 10), as the status
    /// panel's crew and energy are; the battle doesn't read it. Its
    /// <see cref="SilhouetteMarks.Shown"/> are those still showing, which
    /// the snapshot sends.
    /// </summary>
    public SilhouetteMarks Silhouette { get; } = new();

    /// <summary>
    /// ship_data.captain_control.special, where the ship's own code keeps
    /// it: the captain's window's frame its special key shows (the
    /// Shofixti's Glory Device steps it, shofixti_postprocess, from the
    /// frame DrawCaptainsWindow sets as the ship comes in); the null frame
    /// for every other ship, whose window shows DrawCaptainsWindow's. Kept
    /// here, not in the ship's RaceDesc, so a ship gone keeps it: its panel
    /// stands as it last was. The snapshot sends it for the client (stage
    /// 4b item 9).
    /// </summary>
    public Frame CaptainSpecial;
}

/// <summary>
/// The marks ModifySilhouette made on a ship's captain's window since it
/// was last drawn (<see cref="Starship.Silhouette"/>), in order, every one
/// as UQM made it; and those of them still showing (<see cref="Shown"/>):
/// each mark that a later one covers completely left out, so drawing them
/// in order draws the same picture as drawing every mark. A later erase
/// (the window drawn afresh within its rectangle) covers a mark whose
/// rectangle is within its own; a later stamp at the same place covers a
/// stamp whose pixels are all among its own (the same frame, or by their
/// masks); an erase is never covered by a stamp (what it erased would show
/// again where the stamp is clear). The Orz's marines fight on one place
/// many times over (each fight a mark, then the rest frame over it), and
/// die with an erase there, so most of their marks are covered. The rule
/// takes a frame's mask to be exactly the opaque pixels it draws: so it is
/// for every mark frame in the content (the Orz's turret's big sheet keyed
/// on palette index 1, the VUX's slime on index 0), but it wouldn't be for
/// an alpha-blended frame, or a paletted one with no key that draws index 0.
/// </summary>
public sealed class SilhouetteMarks : IReadOnlyList<SilhouetteMark>
{
    private readonly List<SilhouetteMark> _all = new();
    private readonly List<SilhouetteMark> _shown = new();

    /// <summary>The marks still showing, in order.</summary>
    public IReadOnlyList<SilhouetteMark> Shown => _shown;

    public int Count => _all.Count;

    public SilhouetteMark this[int index] => _all[index];

    /// <summary>A mark made: any shown mark it covers completely stops showing.</summary>
    public void Add(SilhouetteMark mark)
    {
        _all.Add(mark);
        _shown.RemoveAll(earlier => Covers(mark, earlier));
        _shown.Add(mark);
    }

    /// <summary>The window drawn afresh (DrawCaptainsWindow): no marks.</summary>
    public void Clear()
    {
        _all.Clear();
        _shown.Clear();
    }

    public IEnumerator<SilhouetteMark> GetEnumerator() => _all.GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>
    /// Whether <paramref name="later"/>, drawn after <paramref name="earlier"/>,
    /// leaves nothing of it showing (see <see cref="SilhouetteMarks"/>); false
    /// where a frame's size or mask isn't known.
    /// </summary>
    public static bool Covers(SilhouetteMark later, SilhouetteMark earlier)
    {
        if (later.Erase)
        {
            if (Box(later) is not { } l || Box(earlier) is not { } e)
                return false;
            return e.X >= l.X && e.Y >= l.Y && e.X + e.W <= l.X + l.W && e.Y + e.H <= l.Y + l.H;
        }
        if (earlier.Erase || (later.X, later.Y) != (earlier.X, earlier.Y))
            return false;
        if (later.Frame == earlier.Frame)
            return true;
        if (later.Frame.Sheet?.Shapes?[later.Frame.Index] is not { Mask: { } } top
                || earlier.Frame.Sheet?.Shapes?[earlier.Frame.Index] is not { Mask: { } } under)
            return false;
        // Each of the earlier frame's pixels under one of the later's, their hot spots at the one place.
        int dx = top.HotSpotX - under.HotSpotX, dy = top.HotSpotY - under.HotSpotY;
        for (int y = 0; y < under.Height; ++y)
            for (int x = 0; x < under.Width; ++x)
            {
                if (!under.Solid(x, y))
                    continue;
                int tx = x + dx, ty = y + dy;
                if (tx < 0 || ty < 0 || tx >= top.Width || ty >= top.Height || !top.Solid(tx, ty))
                    return false;
            }
        return true;
    }

    // A mark's rectangle (GetFrameRect's at its place: its corner minus the hot spot), or null where its size isn't known.
    private static (int X, int Y, int W, int H)? Box(SilhouetteMark m)
    {
        if (m.Frame.Sheet is not { } sheet || (sheet.Rects is null && sheet.Shapes is null))
            return null;
        var r = m.Frame.Rect;
        return (m.X - r.HotSpotX, m.Y - r.HotSpotY, r.Width, r.Height);
    }
}

/// <summary>
/// A mark ModifySilhouette made on a ship's captain's window
/// (<see cref="Starship.Silhouette"/>): <see cref="Frame"/> stamped with
/// its hot spot at (<see cref="X"/>, <see cref="Y"/>) in the ship's status
/// panel (before status_y_offsets for its side), or, with
/// <see cref="Erase"/> (MODIFY_SWAP), the panel drawn afresh within the
/// frame's rectangle there.
/// </summary>
public readonly record struct SilhouetteMark(Frame Frame, short X, short Y, bool Erase);
