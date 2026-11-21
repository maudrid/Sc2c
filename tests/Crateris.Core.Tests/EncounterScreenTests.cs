// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using System.Text.Json.Nodes;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The encounter screen and the battle-won screen (stage 3b-1 item 11)
/// against what UQM's own InitEncounter and UninitEncounter draw
/// (tests/golden/encounter-screens.json): every call in the space window,
/// in order (the status area's, the frame and the message line, are the
/// port's status lines). The battle-won screen runs on its clock to its
/// end with no key, a key held, and Escape held, as UQM's did with its
/// waits and fades in one step.
/// </summary>
[Trait("Topic", "drawing")]
public class EncounterScreenTests
{
    private static readonly JsonElement Root = Golden.Load("encounter-screens");
    private static readonly JsonElement Recorded = Root.GetProperty("scenarios");
    private static readonly Dictionary<string, int> Leading = new() { ["starcon"] = 11, ["tiny"] = 10, ["micro"] = 15 };
    private static readonly URect SpaceWindow = new(UqmCanvas.SisOrgX, UqmCanvas.SisOrgY, UqmCanvas.SisScreenWidth, UqmCanvas.SisScreenHeight);

    public static IEnumerable<object[]> Scenarios(string step) =>
        Recorded.EnumerateArray().Where(s => s.GetProperty("step").GetString()!.StartsWith(step, StringComparison.Ordinal))
            .Select(s => new object[] { s.GetProperty("name").GetString()! });

    public static IEnumerable<object[]> Encounters() => Scenarios("encounter");
    public static IEnumerable<object[]> Wins() => Scenarios("won:");

    private static JsonElement Scenario(string name) =>
        Recorded.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    private static UqmCanvas Canvas() => new(sheet => Root.GetProperty("frames").GetProperty(sheet).GetInt32(), (sheet, i) =>
    {
        var r = Root.GetProperty("rects").GetProperty(sheet)[i].Ints();
        return new URect(r[0], r[1], r[2], r[3]);
    }, Leading);

    private static string Placeholder(int index) => $"{{gs:{index}}}";

    private static IEnumerable<string> SpaceCalls(JsonElement sc) =>
        sc.GetProperty("drawn").EnumerateArray()
            .Where(c => c.EnumerateArray().TakeLast(4).Select(v => v.GetInt32()).ToArray() is [var x, var y, var w, var h]
                        && new URect(x, y, w, h).Equals(SpaceWindow))
            .Select(c => JsonNode.Parse(c.GetRawText())!.ToJsonString());

    private static void Same(IEnumerable<string> want, IEnumerable<UqmDraw> got, string where)
    {
        var w = want.ToList();
        Assert.NotEmpty(w);
        var g = got.Where(d => d.Clip.Equals(SpaceWindow)).Select(StarbaseScreensTests.Json).ToList();
        Assert.All(got, d => Assert.Equal(SpaceWindow, d.Clip));   // nothing drawn outside the window
        for (int i = 0; i < Math.Max(w.Count, g.Count); ++i)
            Assert.True(i < w.Count && i < g.Count && w[i] == g[i],
                $"{where}, call {i}:\n UQM  {(i < w.Count ? w[i] : "(none)")}\n port {(i < g.Count ? g[i] : "(none)")}");
    }

    private static (string Race, string Icons, int Crew)[] Ships(JsonElement sc)
    {
        var ships = BattleReplay.Ships;
        return sc.GetProperty("ships").EnumerateArray().Select(s =>
        {
            string race = s[0].GetString()!;
            string ship = OutfitTests.Rules.RaceShips[OutfitTests.Rules.RaceIndex(race)].Ship;
            return (race, ships[ship].Graphics.Icons!, s[1].GetInt32());
        }).ToArray();
    }

    [Theory]
    [MemberData(nameof(Encounters))]
    public void TheEncounterScreenIsDrawnAsUqms(string name)
    {
        var sc = Scenario(name);
        var ships = Ships(sc);
        bool deep = sc.GetProperty("where").GetString() == "hyper";
        // An infinite fleet's one ship is drawn as many times as InitEncounter draws ships.
        var icons = ships.Length == 1 && ships[0].Crew < 0
            ? Enumerable.Repeat(ships[0].Icons, EncounterScreen.MostShips).ToList()
            : ships.Select(s => s.Icons).ToList();
        var c = Canvas();
        EncounterScreen.Draw(c, Placeholder, deep ? null : "{cluster}", "{planet}", sc.GetProperty("planet").GetString()!, icons);
        Same(SpaceCalls(sc), c.Drawn, name);
    }

    [Theory]
    [MemberData(nameof(Wins))]
    public void TheBattleWonScreenIsDrawnAsUqms(string name)
    {
        var sc = Scenario(name);
        var ships = Ships(sc);
        string keys = sc.GetProperty("step").GetString()!["won:".Length..];
        var rules = OutfitTests.Rules;
        int race = rules.RaceIndex(ships[0].Race);
        uint bounty = (uint)(rules.RaceShips[race].Cost >> 3);
        string shipName = sc.GetProperty("ship_name").GetString()!;
        shipName = shipName[..Math.Min(shipName.Length, 15)];   // GLOBAL_SIS (ShipName): SIS_NAME_SIZE 16
        var c = Canvas();
        long now = 1000;
        var screen = new BattleWonScreen(c, Placeholder, shipName, $"{{gs:{1000 + race}}}", ships[0].Icons, ships.Length, bounty,
            (uint)ships.Length * bounty, now);
        int steps = 0;
        while (!screen.Advance(now += 5, keys != "none", keys == "escape"))
            Assert.True(++steps < 100_000, "it never ends");
        Same(SpaceCalls(sc), c.Drawn, name);
    }

    [Fact]
    public void TheBattleWonScreenTakesUqmsTimeAndAKeyShortensIt()
    {
        // One ship, no key: the fade in (7 twentieths), the ship's flash (10 fifteenths), three seconds, the fade
        // out (7), the debris's fade in (7), two seconds, its fade out (7): about 8.8 seconds of UQM's.
        long Run(Func<long, bool> held)
        {
            var c = Canvas();
            var screen = new BattleWonScreen(c, Placeholder, "S", "{gs:1006}", "ship.spathi.icons", 1, 225, 225, 0);
            long t = 0;
            while (!screen.Advance(t, held(t), false))
                t += 1;
            return t;
        }
        long none = Run(_ => false);
        long expected = 4 * 7 * (840 / 20) + 10 * (840 / 15) + 3 * 840 + 2 * 840;
        Assert.InRange(none, expected - 20, expected + 20);
        // A key pressed and let go during the first wait ends it early.
        long pressed = Run(t => t is > 2000 and < 2050);
        Assert.True(pressed < none - 840, $"{pressed} vs {none}");
        // The ship's pictures in their row: the sixth starts a new one (MAX_DEAD_DISPLAYED).
        var six = Canvas();
        var s6 = new BattleWonScreen(six, Placeholder, "S", "{gs:1006}", "ship.spathi.icons", 6, 225, 600, 0);
        for (long t = 0; !s6.Advance(t, true, true); t += 50)
            Assert.True(t < 100_000);
        var stamps = six.Drawn.Where(d => d.Kind == UqmDrawKind.Stamp && d.Sheet == "ship.spathi.icons" && d.Frame == 0).ToList();
        Assert.Equal(6, stamps.Count);
        Assert.Equal(new[] { 0, 17, 34, 51, 68, 0 }, stamps.Select(s => s.X - stamps[0].X));
        // Escape held: no debris; the share, not the whole, is what's scavenged.
        Assert.DoesNotContain(six.Drawn, d => d.Text?.Contains("RU", StringComparison.Ordinal) == true);
        var shared = Canvas();
        var s = new BattleWonScreen(shared, Placeholder, "S", "{gs:1006}", "ship.spathi.icons", 2, 225, 150, 0);
        for (long t = 0; !s.Advance(t, true, false); t += 50)
            Assert.True(t < 100_000);
        Assert.Contains(shared.Drawn, d => d.Text == "450");   // the count: each ship's bounty
        Assert.Contains(shared.Drawn, d => d.Text == $"150 {Placeholder(Content.GameStrings.StatusBase + 1)}");   // the share
    }

    [Fact]
    public void TheGravityWellIsTheBattlePlanetsBigView()
    {
        var planets = Data.PlanetTypes.Load(Path.Combine(AppContext.BaseDirectory, "data"));
        // A data_index's type, or a shielded world's whatever its type (load_gravity_well).
        byte water = (byte)Enumerable.Range(0, planets.Count).First(i => planets[i].BattleSprites[0] == "planet.water.large");
        Assert.Equal("planet.water.large", EncounterScreen.WellSheet(water, planets));
        byte shielded = (byte)Enumerable.Range(0, 256).First(i => Systems.WorldTypes.IsShielded((byte)i));
        Assert.Equal("planet.slaveshield.large", EncounterScreen.WellSheet(shielded, planets));
    }

    [ContentFact]
    public void TheTitleNamesTheClusterAndThePlanetsOwnName()
    {
        var names = TestContent.With(p => Content.Names.Load(p));
        var map = SystemGroupTests.Systems.Value.Map;
        var ships = new[] { new World.EnemyFleetShip("ilwrath") };
        var deep = new World.MeetingView(1, World.MeetingStep.Encounter, "ILWRATH_SHIP", ships);
        Assert.Equal(((string?)null, ""), EncounterScreen.Place(deep, map, names));
        // A star with a planet that has a moon.
        var star = map.Stars.First(s => SystemGroupTests.Systems.Value[s.Number].Planets.Count > 0
                                        && SystemGroupTests.Systems.Value[s.Number].Moons(0).Length > 0);
        string planet = names.PlanetName(star, 0), moon = names.PlanetOrMoon(star, 0, 0);
        Assert.NotEqual(planet, moon);
        // At a world's moon, the planet's own name (generateName: never a moon's); a group met, the planet named.
        var atMoon = deep with { Star = star.Number, Planet = 0, Moon = 0 };
        Assert.Equal((names.Cluster(star), planet), EncounterScreen.Place(atMoon, map, names));
        var met = deep with { Star = star.Number, Named = 0 };
        Assert.Equal((names.Cluster(star), planet), EncounterScreen.Place(met, map, names));
        Assert.Equal((names.Cluster(star), ""), EncounterScreen.Place(deep with { Star = star.Number }, map, names));
    }

    [Fact]
    public void ShipsPastTheTenthGoRoundFurtherOut()
    {
        Assert.Equal((10, 51), EncounterScreen.ShipPoint(0));
        Assert.Equal((-44, -27), EncounterScreen.ShipPoint(9));
        var (x, y) = EncounterScreen.ShipPoint(10);
        int r = (int)Math.Round(Math.Sqrt(x * x + y * y));
        Assert.InRange(r, 52 + 18 - 2, 52 + 18 + 2);   // square_root (10² + 51²) + 18
    }
}
