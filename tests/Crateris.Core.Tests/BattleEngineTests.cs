// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Combat;
using Crateris.Core.Uqm;

namespace Crateris.Core.Tests;

/// <summary>
/// The battle engine against UQM's own (tests/golden/battle.json, recorded
/// by tools/extract/golden/battle.c running UQM's battle.c, process.c,
/// collide.c, gravity.c, misc.c, weapon.c, ship.c, tactrans.c and init.c),
/// with test ships: each battle replayed from its seed and keys, every
/// frame's random generator, camera, ships, elements (as the netplay
/// checksum takes them) and random draws compared (<see cref="BattleReplay"/>).
/// </summary>
[Trait("Topic", "combat")]
public class BattleEngineTests
{
    private static readonly Lazy<JsonElement> Reference = new(() => Golden.Load("battle"));

    public static IEnumerable<object[]> Battles() => BattleReplay.Names("battle");

    [ContentTheory]
    [MemberData(nameof(Battles))]
    public void TheBattleIsUqms(string name)
    {
        var input = BattleReplay.Recorded(Reference.Value, name).GetProperty("input");
        var ships = input.GetProperty("ships").EnumerateArray().ToList();
        int[] fleetSizes = input.TryGetProperty("fleet", out var fleet) ? fleet.Ints() : new[] { 1, 1 };
        var fleets = ships.Select((s, side) => (IReadOnlyList<Starship>)Enumerable.Range(0, fleetSizes[side]).Select(_ =>
            new Starship(new TestShip(s), (short)side, (ushort)s.GetProperty("crew").GetInt32(),
                (ushort)s.GetProperty("max_crew").GetInt32()) { CaptainsNameIndex = (byte)(side == 0 ? 0 : 5) }).ToList()).ToList();
        bool runaway = input.TryGetProperty("runaway", out var r) && r.GetBoolean();
        var battle = BattleReplay.Run(Reference.Value, name, fleets, runaway, colours: false);
        BattleReplay.End(Reference.Value, name, battle);
    }

    /// <summary>
    /// The recording's test ship (battle.c's load_ship and test_weapons): a
    /// RACE_DESC with the stock ship code and plain weapons, flying a
    /// ship's sheets with its numbers; with a cyborg_control (weapon_range,
    /// maneuverability) when the AI flies it, with the stock ship_intelligence.
    /// </summary>
    internal sealed class TestShip : IShipCode
    {
        private const int Missile = 1, Homing = 2, Laser = 4, Spray = 8;
        private readonly JsonElement _input;
        private readonly Views _hull, _weapon;

        public TestShip(JsonElement input)
        {
            _input = input;
            var graphics = BattleReplay.Ships[input.GetProperty("ship").GetString()!].Graphics;
            _hull = BattleReplay.ViewsOf(graphics.Ship);
            _weapon = BattleReplay.ViewsOf(graphics.Weapon);
        }

        private int Get(string name) => _input.GetProperty(name).GetInt32();

        private int Characteristic(string name) => _input.GetProperty("characteristics").GetProperty(name).GetInt32();

        public RaceDesc Load(Battle battle, Starship starship)
        {
            var rd = new RaceDesc
            {
                ShipFlags = (ShipFlags)Get("flags"), CrewLevel = (ushort)Get("crew"), MaxCrew = (ushort)Get("max_crew"),
                EnergyLevel = (byte)Get("energy"), MaxEnergy = (byte)Get("max_energy"),
                Characteristics = Characteristics.From(new ShipCharacteristics(
                    Characteristic("max_thrust"), Characteristic("thrust_increment"), Characteristic("energy_regeneration"),
                    Characteristic("weapon_energy_cost"), Characteristic("special_energy_cost"), Characteristic("energy_wait"),
                    Characteristic("turn_wait"), Characteristic("thrust_wait"), Characteristic("weapon_wait"),
                    Characteristic("special_wait"), Characteristic("ship_mass"))),
                Ship = _hull, Weapon = _weapon,
            };
            rd.InitWeapon = ship => Weapons(battle, ship);
            if (_input.TryGetProperty("weapon_range", out var range))
                rd.WeaponRange = (ushort)range.GetInt32();
            if (_input.TryGetProperty("maneuverability", out var maneuverability))
                rd.ManeuverabilityIndex = (ushort)maneuverability.GetInt32();
            rd.Intelligence = battle.ShipIntelligence;
            return rd;
        }

        private IReadOnlyList<Element?> Weapons(Battle battle, Element ship)
        {
            var starShip = ship.Parent!;
            int weapons = Get("weapons");
            var made = new List<Element?>();
            if ((weapons & Spray) != 0)
            {
                for (int i = 0; i < 6; ++i)
                    made.Add(Fire(battle, ship, starShip.ShipFacing + i * 8 / 3, false));
                return made;
            }
            if ((weapons & Missile) != 0)
                made.Add(Fire(battle, ship, starShip.ShipFacing, false));
            if ((weapons & Homing) != 0)
                made.Add(Fire(battle, ship, starShip.ShipFacing, true));
            if ((weapons & Laser) != 0)
            {
                int angle = UqmMath.FacingToAngle(starShip.ShipFacing);
                var block = new LaserBlock
                {
                    Cx = ship.Next.X, Cy = ship.Next.Y, Face = starShip.ShipFacing,
                    Ex = UqmMath.Cosine(angle, Battle.DisplayToWorld(Get("laser_range"))),
                    Ey = UqmMath.Sine(angle, Battle.DisplayToWorld(Get("laser_range"))),
                    Sender = ship.PlayerNr, Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)Get("offset"),
                    Color = Battle.Rgb15(0x1F, 0x1F, 0x1F),
                };
                made.Add(battle.InitializeLaser(block));
            }
            return made;
        }

        private Element? Fire(Battle battle, Element ship, int face, bool homing)
        {
            ushort facing = UqmMath.NormalizeFacing(face);
            var block = new MissileBlock
            {
                Cx = ship.Next.X, Cy = ship.Next.Y, FrameArray = _weapon, Face = facing, Index = facing,
                Sender = ship.PlayerNr, Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)Get("offset"),
                Speed = (short)Get("speed"), HitPoints = (short)Get("hits"), Damage = (short)Get("damage"),
                Life = (ushort)Get("life"), PreprocessFunc = homing ? e => HomingPreprocess(battle, e) : null,
            };
            var missile = battle.InitializeMissile(block);
            if (missile != null && homing)
                missile.TurnWait = (byte)Get("track_wait");
            return missile;
        }

        // battle.c's homing_preprocess: a facing towards the target every track_wait + 1 frames.
        private void HomingPreprocess(Battle battle, Element e)
        {
            if (e.TurnWait > 0)
                --e.TurnWait;
            else
            {
                ushort facing = UqmMath.NormalizeFacing(UqmMath.AngleToFacing(e.Velocity.TravelAngle));
                if (battle.TrackShip(e, ref facing) > 0)
                {
                    e.Next.Frame = e.Current.Frame.SetAbs(facing);
                    e.StateFlags |= ElementFlags.Changing;
                    e.Velocity.SetVector(Get("speed"), facing);
                }
                e.TurnWait = (byte)Get("track_wait");
            }
        }
    }
}
