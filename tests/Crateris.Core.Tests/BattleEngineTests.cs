// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Crateris.Core.Combat;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Uqm;

namespace Crateris.Core.Tests;

/// <summary>
/// The battle engine against UQM's own (tests/golden/battle.json, recorded
/// by tools/extract/golden/battle.c running UQM's battle.c, process.c,
/// collide.c, gravity.c, misc.c, weapon.c, ship.c, tactrans.c and init.c):
/// each battle replayed from its seed and keys, every frame's random
/// generator, camera, ships, elements (as the netplay checksum takes them)
/// and random draws compared.
/// </summary>
[Trait("Topic", "combat")]
public class BattleEngineTests
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly PlanetTypes Planets = PlanetTypes.Load(Data);
    private static readonly ShipDescriptions Ships = ShipDescriptions.Load(Data);
    private static readonly ShipDescription[] Fighters = { Ships[ShipDescriptions.Flagship], Ships[ShipDescriptions.Ilwrath] };
    private static readonly Lazy<JsonElement> Reference = new(() => Golden.Load("battle"));

    private static readonly Lazy<BattleSheets> Sheets = new(() => TestContent.With(p =>
    {
        var decoded = new AnimationSheets(p);
        foreach (string id in BattleResources.All(Fighters, Planets))
            _ = decoded[id];
        return new BattleSheets(decoded, BattleShapes.Load(decoded, Fighters, Planets));
    }));

    public static IEnumerable<object[]> Battles() =>
        Golden.Load("battle").GetProperty("battles").EnumerateArray().Select(b => new object[] { b.GetProperty("name").GetString()! });

    [ContentTheory]
    [MemberData(nameof(Battles))]
    public void TheBattleIsUqms(string name)
    {
        var reference = Reference.Value;
        var recorded = reference.GetProperty("battles").EnumerateArray().First(b => b.GetProperty("name").GetString() == name);
        var input = recorded.GetProperty("input");
        var sheetNumbers = reference.GetProperty("sheets").EnumerateArray()
            .Select((s, i) => (s.GetString()!, i)).ToDictionary(x => x.Item1, x => x.i);
        var sheets = Sheets.Value;
        string planet = input.GetProperty("planet").GetString()!;
        var arena = sheets.Arena(new[] { $"{planet}.large", $"{planet}.medium", $"{planet}.small" });

        var ships = input.GetProperty("ships").EnumerateArray().ToList();
        var fleets = ships.Select((s, side) => (IReadOnlyList<Starship>)new[]
        {
            new Starship(new TestShip(s, sheets), (short)side, (ushort)s.GetProperty("crew").GetInt32(),
                (ushort)s.GetProperty("max_crew").GetInt32()) { CaptainsNameIndex = (byte)(side == 0 ? 0 : 5) },
        }).ToList();
        var places = new List<string>();
        var zoom = input.TryGetProperty("zoom", out var z) && z.GetString() == "step" ? BattleZoom.Step : BattleZoom.Smooth;
        bool runaway = input.TryGetProperty("runaway", out var r) && r.GetBoolean();
        var battle = new Battle(arena, fleets, input.GetProperty("seed").GetUInt32(), zoom, runaway, places);

        // InitGalaxy's stars.
        Assert.Equal(recorded.GetProperty("stars").EnumerateArray().Select(p => (p[0].GetInt32(), p[1].GetInt32())),
            battle.Stars.Select(p => ((int)p.X, (int)p.Y)));

        var frames = recorded.GetProperty("frames").EnumerateArray().ToList();
        Check(name, 0, frames[0], battle, places, sheetNumbers);
        var events = input.TryGetProperty("events", out var ev)
            ? ev.EnumerateArray().Select(e => e.Ints()).ToList() : new List<int[]>();
        int frame = 0;
        foreach (var step in input.GetProperty("steps").EnumerateArray().Select(s => s.Ints()))
        {
            for (int i = 0; i < step[0] && battle.InBattle; ++i, ++frame)
            {
                // The draws an event makes count in the frame it comes before.
                places.Clear();
                foreach (var e in events.Where(e => e[0] == frame))
                    battle.AbandonShip(battle.Fleet(e[1])[0].Ship!, battle.Fleet(e[2])[0].Ship!, e[3]);
                battle.Step(new[] { (BattleInput)step[1], (BattleInput)step[2] });
                Assert.True(frame + 1 < frames.Count, $"{name}: the battle runs past UQM's {frames.Count - 1} frames");
                Check(name, frame + 1, frames[frame + 1], battle, places, sheetNumbers);
            }
        }
        Assert.Equal(frames.Count - 1, frame);
        Assert.Equal(recorded.GetProperty("ended").GetBoolean(), !battle.InBattle);
    }

    private static void Check(string name, int n, JsonElement want, Battle battle, List<string> places,
        IReadOnlyDictionary<string, int> sheetNumbers)
    {
        string at = $"{name} frame {n}";
        string draws = "[" + string.Join(",", places.Select(p => $"\"{p}\"")) + "]";
        bool full = want.GetArrayLength() == 10;
        if (full)
            Assert.True(want[9].GetRawText() == draws, $"{at}: draws {Runs(places)}, UQM {Runs(want[9].EnumerateArray().Select(p => p.GetString()!))}");
        Assert.True(want[0].GetUInt32() == battle.Random.Seed, $"{at}: seed {battle.Random.Seed}, UQM {want[0]}");
        Assert.True((want[1].GetInt32(), want[2].GetInt32()) == (battle.ScrollX, battle.ScrollY),
            $"{at}: scroll ({battle.ScrollX}, {battle.ScrollY}), UQM ({want[1]}, {want[2]})");
        Assert.True(want[3].GetInt32() == battle.ZoomOut, $"{at}: zoom {battle.ZoomOut}, UQM {want[3]}");
        Assert.True(want[4].GetInt32() == battle.ViewState, $"{at}: view {battle.ViewState}, UQM {want[4]}");
        Assert.True((want[5].GetInt32(), want[6].GetInt32()) == (battle.SpaceOrgX, battle.SpaceOrgY),
            $"{at}: the view's corner ({battle.SpaceOrgX}, {battle.SpaceOrgY}), UQM ({want[5]}, {want[6]})");

        string ships = ShipsText(battle);
        var elements = battle.List.Elements.Select(e => ElementText(e, sheetNumbers)).ToList();
        if (!full)
        {
            // A long battle's later frames: the count and a hash.
            Assert.True(want[7].GetInt32() == elements.Count, $"{at}: {elements.Count} elements, UQM {want[7]}");
            string text = "[" + ships + ",[" + string.Join(",", elements) + "]," + draws + "]";
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant()[..16];
            Assert.True(want[8].GetString() == hash, $"{at}: the frame differs from UQM's (hashed): {text}");
            return;
        }
        Assert.True(want[7].GetRawText() == ships, $"{at}: ships {ships}, UQM {want[7].GetRawText()}");
        var wantElements = want[8].EnumerateArray().Select(e => e.GetRawText()).ToList();
        for (int i = 0; i < Math.Min(wantElements.Count, elements.Count); ++i)
            Assert.True(wantElements[i] == elements[i], $"{at}: element {i} is {elements[i]}, UQM {wantElements[i]}");
        Assert.True(wantElements.Count == elements.Count, $"{at}: {elements.Count} elements, UQM {wantElements.Count}");
    }

    private static string ShipsText(Battle battle) => "[" + string.Join(",", Enumerable.Range(0, Battle.NumSides).Select(side =>
    {
        var s = battle.Fleet(side)[0];
        var rd = s.RaceDesc;
        return rd == null ? "null" : Ints(rd.CrewLevel, rd.EnergyLevel, s.ShipFacing, s.EnergyCounter, s.WeaponCounter,
            s.SpecialCounter, (int)s.CurStatusFlags, s.Ship != null ? 1 : 0);
    })) + "]";

    private static string ElementText(Element e, IReadOnlyDictionary<string, int> sheetNumbers)
    {
        var v = e.Velocity;
        return Ints((int)e.StateFlags, e.LifeSpan, e.CrewLevel, e.MassPoints, e.TurnWait, e.ThrustWait,
            v.TravelAngle, v.VectorWidth, v.VectorHeight, v.FractWidth, v.FractHeight, v.ErrorWidth, v.ErrorHeight,
            v.IncrWidth, v.IncrHeight, e.Current.X, e.Current.Y, e.Next.X, e.Next.Y,
            e.Current.Frame.Sheet is { } sheet ? sheetNumbers[sheet.Id] : -1, e.Current.Frame.Index, e.PlayerNr,
            (int)e.Prim, e.Prim == PrimType.StampFill && e.PrimColor == 0 ? 1 : 0, e.Slot, e.Target?.Slot ?? 0);
    }

    // Draws as runs of the same place, "place x count".
    private static string Runs(IEnumerable<string> places)
    {
        var runs = new List<(string Place, int Count)>();
        foreach (string p in places)
            if (runs.Count > 0 && runs[^1].Place == p)
                runs[^1] = (p, runs[^1].Count + 1);
            else
                runs.Add((p, 1));
        return string.Join(", ", runs.Select(r => $"{r.Place} x {r.Count}"));
    }

    private static string Ints(params int[] values) => "[" + string.Join(",", values) + "]";

    /// <summary>
    /// The recording's test ship (battle.c's load_ship and test_weapons): a
    /// RACE_DESC with the stock ship code and plain weapons, flying a
    /// ship's sheets with its numbers.
    /// </summary>
    private sealed class TestShip : IShipCode
    {
        private const int Missile = 1, Homing = 2, Laser = 4, Spray = 8;
        private readonly JsonElement _input;
        private readonly Views _hull, _weapon;

        public TestShip(JsonElement input, BattleSheets sheets)
        {
            _input = input;
            var graphics = Ships[input.GetProperty("ship").GetString()!].Graphics;
            _hull = sheets.Views(graphics.Ship)!;
            _weapon = sheets.Views(graphics.Weapon)!;
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
