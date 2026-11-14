// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/loadship.c: load_ship's
// RACE_DESC made from a ship's init function), Copyright Paul Reiche,
// Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Combat;

/// <summary>
/// The race ships whose code is ported, made by their ids in
/// data/ships.json (UQM's ships/ folder names), as load_ship makes each
/// from its init_RACE: the Ilwrath (stage 4a) and stage 4b's, part by part.
/// </summary>
public static class RaceShips
{
    /// <summary>The race ships ported, by id: what a battle can be fought against (<c>--start-battle</c>).</summary>
    public static readonly IReadOnlyList<string> Ported = new[]
    {
        ShipDescriptions.Ilwrath,
        // Stage 4b item 2.
        ArilouShip.Id, SpathiShip.Id, SupoxShip.Id, DruugeShip.Id, YehatShip.Id, ZoqFotPikShip.Id, ThraddashShip.Id,
        UmgahShip.Id,
        // Stage 4b item 3.
        UtwigShip.Id, MyconShip.Id, AndrosynthShip.Id, MmrnmhrmShip.Id, ChenjesuShip.Id, SlylandroShip.Id,
        UrquanShip.Id, KohrAhShip.Id,
        // Stage 4b item 4.
        HumanShip.Id, SyreenShip.Id, PkunkShip.Id, VuxShip.Id,
    };

    /// <summary>
    /// A ported race ship's code from its description, its sheets from
    /// <paramref name="views"/> (a RACE_DESC's three sizes, the same
    /// instance for the same sheets: <see cref="BattleSheets.Views"/>).
    /// <paramref name="noCloak"/> is the Ilwrath's Earth fight's rule.
    /// </summary>
    public static IShipCode Code(ShipDescription d, Func<IReadOnlyList<string?>, Views?> views, bool noCloak = false)
    {
        Views? hull = views(d.Graphics.Ship), weapon = views(d.Graphics.Weapon), special = views(d.Graphics.Special);
        return d.Id switch
        {
            ShipDescriptions.Ilwrath => new IlwrathShip(d, hull, weapon, noCloak),
            ArilouShip.Id => new ArilouShip(d, hull, special),
            SpathiShip.Id => new SpathiShip(d, hull, weapon, special),
            SupoxShip.Id => new SupoxShip(d, hull, weapon),
            DruugeShip.Id => new DruugeShip(d, hull, weapon),
            YehatShip.Id => new YehatShip(d, hull, weapon, special),
            ZoqFotPikShip.Id => new ZoqFotPikShip(d, hull, weapon, special),
            ThraddashShip.Id => new ThraddashShip(d, hull, weapon, special),
            UmgahShip.Id => new UmgahShip(d, hull, weapon, special),
            UtwigShip.Id => new UtwigShip(d, hull, weapon),
            MyconShip.Id => new MyconShip(d, hull, weapon),
            AndrosynthShip.Id => new AndrosynthShip(d, hull, weapon, special),
            MmrnmhrmShip.Id => new MmrnmhrmShip(d, hull, weapon, special),
            ChenjesuShip.Id => new ChenjesuShip(d, hull, weapon, special),
            SlylandroShip.Id => new SlylandroShip(d, hull),
            UrquanShip.Id => new UrquanShip(d, hull, weapon, special),
            KohrAhShip.Id => new KohrAhShip(d, hull, weapon, special),
            HumanShip.Id => new HumanShip(d, hull, weapon),
            SyreenShip.Id => new SyreenShip(d, hull, weapon),
            PkunkShip.Id => new PkunkShip(d, hull, weapon),
            VuxShip.Id => new VuxShip(d, hull, weapon, special),
            _ => throw new ArgumentException($"{d.Id}'s code isn't ported (ported: {string.Join(", ", Ported)})"),
        };
    }

    /// <summary>
    /// A ship's status panel's icons (ship_info.icons) from <paramref name="views"/>,
    /// for its <see cref="Starship.Icons"/>; null for a ship without them.
    /// </summary>
    public static Views? Icons(ShipDescription d, Func<IReadOnlyList<string?>, Views?> views) =>
        d.Graphics.Icons is { } icons ? views(new[] { icons, null, null }) : null;

    /// <summary>
    /// A RACE_DESC as a ship's file initialises it (data/ships.json's
    /// description: SHIP_INFO, CHARACTERISTIC_STUFF, INTEL_STUFF) with its
    /// sheets; the ship's callbacks are its own to set.
    /// </summary>
    internal static RaceDesc NewRaceDesc(ShipDescription d, Views? hull, Views? weapon, Views? special) => new()
    {
        ShipFlags = d.Flags, CrewLevel = (ushort)d.Crew, MaxCrew = (ushort)d.MaxCrew,
        EnergyLevel = (byte)d.Energy, MaxEnergy = (byte)d.MaxEnergy,
        Characteristics = Characteristics.From(d.Characteristics),
        Ship = hull, Weapon = weapon, Special = special,
        ManeuverabilityIndex = (ushort)d.Intel.ManeuverabilityIndex,
        WeaponRange = (ushort)d.Intel.WeaponRange,
    };

    /// <summary>Refuses a description that isn't the ship's own.</summary>
    internal static ShipDescription Check(ShipDescription d, string id) => d.Id == id
        ? d : throw new ArgumentException($"{d.Id} isn't the {id}'s description", nameof(d));
}
