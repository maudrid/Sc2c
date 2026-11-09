// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Crateris.Core.Content;
using Crateris.Core.Starbase;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The starbase's screens (docs/plans/stage-3.md, item 11) against what
/// UQM's own starbase.c, outfit.c, shipyard.c, sis.c, menu.c and border.c
/// draw (tests/golden/screens.json). Each scenario's keys move the port's
/// menus (<see cref="OutfitMenu"/>, <see cref="ShipyardMenu"/>, the
/// starbase's turning and the power lines on the clock) as UQM's moved,
/// and every step draws what UQM's drew, call for call, with the flashing
/// highlight in the same place and the power lines in the same colour
/// map. Both are painted from the content too (<see cref="ScreenRaster"/>),
/// which paints every call the screens make (the same calls give the same
/// picture: it checks the painter takes them all, not UQM's pixels).
/// </summary>
[Trait("Topic", "drawing")]
public class StarbaseScreensTests
{
    private static readonly JsonElement Root = Golden.Load("screens");
    private static readonly JsonElement Recorded = Root.GetProperty("scenarios");
    private static readonly Dictionary<string, int> Leading = new() { ["starcon"] = 11, ["tiny"] = 10, ["micro"] = 15 };

    public static IEnumerable<object[]> Scenarios() =>
        Recorded.EnumerateArray().Select(s => new object[] { s.GetProperty("name").GetString()! });

    private static JsonElement Scenario(string name) =>
        Recorded.EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);

    private static int FrameCount(string sheet) => Root.GetProperty("frames").GetProperty(sheet).GetInt32();

    // GetFrameRect, as recorded from the content.
    private static URect FrameRect(string sheet, int i)
    {
        var r = Root.GetProperty("rects").GetProperty(sheet)[i].Ints();
        return new URect(r[0], r[1], r[2], r[3]);
    }

    internal static UqmCanvas Canvas() => new(FrameCount, FrameRect, Leading);

    internal static string Placeholder(int index) => $"{{gs:{index}}}";

    // A drawing call as golden/screens.c writes it.
    internal static string Json(UqmDraw d)
    {
        var call = d.Kind switch
        {
            UqmDrawKind.Stamp => new JsonArray("s", d.Sheet, d.Frame, d.X, d.Y),
            UqmDrawKind.FilledStamp => new JsonArray("f", d.Sheet, d.Frame, d.X, d.Y, d.Color.R, d.Color.G, d.Color.B),
            UqmDrawKind.Rect => new JsonArray("r", d.X, d.Y, d.Width, d.Height, d.Color.R, d.Color.G, d.Color.B),
            UqmDrawKind.Outline => new JsonArray("o", d.X, d.Y, d.Width, d.Height, d.Color.R, d.Color.G, d.Color.B),
            UqmDrawKind.Point => new JsonArray("p", d.X, d.Y, d.Color.R, d.Color.G, d.Color.B),
            UqmDrawKind.Clear => new JsonArray("c", d.X, d.Y, d.Width, d.Height, d.Color.R, d.Color.G, d.Color.B),
            _ => new JsonArray("t", d.Font, d.X, d.Y, (int)d.Align, d.Count, d.Text, d.Color.R, d.Color.G, d.Color.B, d.Effect),
        };
        foreach (int v in new[] { d.Clip.X, d.Clip.Y, d.Clip.W, d.Clip.H })
            call.Add(v);
        return call.ToJsonString();
    }

    private static void Same(IEnumerable<JsonElement> want, IEnumerable<UqmDraw> got, string where)
    {
        var w = want.Select(e => JsonNode.Parse(e.GetRawText())!.ToJsonString()).ToList();
        var g = got.Select(Json).ToList();
        for (int i = 0; i < Math.Max(w.Count, g.Count); ++i)
            Assert.True(i < w.Count && i < g.Count && w[i] == g[i],
                $"{where}, call {i}:\n UQM  {(i < w.Count ? w[i] : "(none)")}\n port {(i < g.Count ? g[i] : "(none)")}");
    }

    /// <summary>
    /// The port's screens, moved step by step as a scenario's steps moved
    /// UQM's, as the game moves them: the menus take the keys and list
    /// what to draw, the host's drawing goes as its message (each key's
    /// with the ship as the key left it, <see cref="StarbaseDrawing"/>),
    /// and the client's picture (<see cref="StarbasePicture"/>) draws it
    /// over what it drew before, the starbase turning and the power lines
    /// changing on its clock.
    /// </summary>
    private sealed class Replay
    {
        private enum On { Nothing, Base, Outfit, Yard }

        private readonly JsonElement _sc;
        private readonly OutfitTests.TestShip _ship;
        private readonly StoryState _story;
        private readonly Outfitting _rules;
        private readonly List<UqmDraw> _drawn = new();
        private OutfitMenu? _outfit;
        private ShipyardMenu? _yard;
        private int _baseState;
        private long _now;
        private On _on;

        public Replay(JsonElement sc)
        {
            _sc = sc;
            (_ship, _story) = OutfitTests.Start(sc);
            _rules = new Outfitting(_ship, _story);
            Picture = new StarbasePicture(Canvas(), Placeholder, FrameCount(StarbaseScreens.StarbaseSheet),
                Root.GetProperty("hangar_maps").GetInt32(), s => SisState.Of(s, "", ""));
        }

        public StarbasePicture Picture { get; }

        public StarbaseScreens Screens => Picture.Screens;

        /// <summary>Whether a screen is open (outfitting or the shipyard left: none, until the next opens).</summary>
        public bool Open => _on != On.Nothing;

        /// <summary>The step's drawing: the calls it added.</summary>
        public List<UqmDraw> Step(string step)
        {
            _drawn.Clear();
            Run(step);
            return new List<UqmDraw>(_drawn);
        }

        private void Run(string step)
        {
            if (step.StartsWith("base:"))
            {
                _baseState = int.Parse(step[5..]);
                _on = On.Base;
                Send(null, new() { new(ScreenOpKind.MenuOpened, _baseState) });
                return;
            }
            if (step == "outfit")
            {
                _outfit = new OutfitMenu(_rules, _rules.Apply);
                _outfit.Open();
                _on = On.Outfit;
                Send(Ship(), _outfit.TakeOps());
                return;
            }
            if (step == "shipyard")
            {
                _yard = new ShipyardMenu(_rules, _rules.Apply);
                _on = On.Yard;
                Send(Ship(), _yard.TakeOps());
                return;
            }
            MenuKey? key = null;
            if (step.StartsWith("t:"))
                _now = long.Parse(step[2..]);
            else
                key = step switch
                {
                    "u" => MenuKey.Up,
                    "d" => MenuKey.Down,
                    "l" => MenuKey.Left,
                    "r" => MenuKey.Right,
                    "s" => MenuKey.Select,
                    "c" => MenuKey.Cancel,
                    "pu" => MenuKey.PageUp,
                    "pd" => MenuKey.PageDown,
                    _ => throw new InvalidDataException($"no step {step}"),
                };
            switch (_on)
            {
                case On.Base:
                    // DoStarBase: the cursor round its four entries, then the starbase turned.
                    int was = _baseState;
                    if (key is MenuKey.Left or MenuKey.Up)
                        _baseState = (_baseState + 3) % 4;
                    else if (key is MenuKey.Right or MenuKey.Down)
                        _baseState = (_baseState + 1) % 4;
                    if (_baseState != was)
                        Send(null, new() { new(ScreenOpKind.MenuMoved, was, _baseState) });
                    _drawn.AddRange(Picture.Tick(_now));
                    break;
                case On.Outfit when key is { } k:
                    if (_outfit!.Key(k) == OutfitOutcome.Left)
                        _on = On.Nothing;
                    Send(Ship(), _outfit.TakeOps());
                    break;
                case On.Yard:
                    _drawn.AddRange(Picture.Tick(_now));   // on_input_frame, then the screen
                    if (key is { } y && _yard!.Key(y) == OutfitOutcome.Left)
                        _on = On.Nothing;
                    Send(Ship(), _yard!.TakeOps());
                    break;
            }
        }

        // The host's drawing for a key, through its message, drawn on the client's picture.
        private void Send(StarbaseShip? ship, List<ScreenOp> ops)
        {
            var sent = StarbaseDrawing.Decode(StarbaseDrawing.Encode(new[] { new StarbaseDrawing(ship, ops) }));
            _drawn.AddRange(Picture.Draw(sent, _now));
        }

        /// <summary>Where the flashing highlight is now (none on the starbase's menu).</summary>
        public URect? Flash => _on switch
        {
            On.Outfit => StarbaseScreens.OutfitFlash(_outfit!.State, _outfit.Item, _outfit.Installing),
            On.Yard => StarbaseScreens.YardFlash(_yard!.Mode, _yard.Cursor),
            _ => null,
        };

        // The flagship and the prices as the host sends them with a drawing.
        private StarbaseShip Ship()
        {
            var ship = _sc.GetProperty("ship");
            var state = _sc.GetProperty("state");
            int State(string f) => state.TryGetProperty(f, out var v) ? v.GetInt32() : 0;
            return new StarbaseShip(_ship.Modules.ModuleSlots.ToArray(), _ship.Modules.DriveSlots.ToArray(),
                _ship.Modules.JetSlots.ToArray(), _ship.Crew, _ship.Fuel, _ship.Landers, _ship.ResourceUnits, _ship.MineralMass,
                ship.GetProperty("ship").GetString()!, ship.GetProperty("captain").GetString()!, State("CHMMR_BOMB_STATE"),
                State("LANDER_SHIELDS"), _story.FuelCost, _story.CrewCost,
                ShipModules.Names.Select(m => OutfitTests.Rules.Modules.Contains(m) ? _story.ModuleCost(m) : 0).ToArray(),
                OutfitTests.Rules.ModuleCostScale);
        }
    }

    private static string Where(string name, int i, string step) => $"{name} step {i} ({step})";

    [Theory]
    [MemberData(nameof(Scenarios))]
    public void EveryStepDrawsCallForCallAsUqmsAndFlashesWhereUqmsDoes(string name)
    {
        var sc = Scenario(name);
        var steps = sc.GetProperty("steps").EnumerateArray().Select(s => s.GetString()!).ToList();
        var drawn = sc.GetProperty("drawn");
        var flash = sc.GetProperty("flash");
        var cmap = sc.GetProperty("cmap");
        var port = new Replay(sc);
        for (int i = 0; i < steps.Count; ++i)
        {
            Same(drawn[i].EnumerateArray(), port.Step(steps[i]), Where(name, i, steps[i]));
            if (!port.Open)
                continue;
            var want = flash[i].ValueKind == JsonValueKind.Null ? (URect?)null : Rect(flash[i].Ints());
            Assert.True(want.Equals(port.Flash), $"{Where(name, i, steps[i])}: flash {port.Flash}, UQM {want}");
            if (steps[0] == "shipyard")
                Assert.True(cmap[i].GetInt32() == port.Screens.ColorMap,
                    $"{Where(name, i, steps[i])}: power lines in map {port.Screens.ColorMap}, UQM {cmap[i].GetInt32()}");
        }
    }

    private static URect Rect(int[] r) => new(r[0], r[1], r[2], r[3]);

    // ------------------------------------------------------------ the pictures

    /// <summary>The content the pictures are painted from: the frames in the colour maps, the fonts, the game's strings.</summary>
    private sealed class Pictures
    {
        private readonly AnimationSheets _sheets;
        private readonly ColorMaps _main;
        private readonly BinaryTable _hangar;
        private readonly GameStrings _strings;
        private readonly Dictionary<string, UqmFont> _fonts;
        private readonly ConcurrentDictionary<(string, int, int), SpriteImage> _frames = new();
        private readonly ConcurrentDictionary<string, SpriteSheet> _loaded = new();

        public Pictures(ContentPackage p)
        {
            _sheets = new AnimationSheets(p);
            _main = new ColorMaps();
            _main.Set(BinaryTable.Load(p, "colortable.main")[0]);
            _hangar = BinaryTable.Load(p, StarbaseScreens.HangarColorTable);
            _strings = GameStrings.Load(p);
            _fonts = new() { ["starcon"] = UqmFont.Load(p, "font.starcon"), ["tiny"] = UqmFont.Load(p, "font.tiny"),
                ["micro"] = UqmFont.Load(p, "font.micro") };
            HangarMaps = _hangar.Count;
        }

        public int HangarMaps { get; }

        /// <summary>A frame painted with colortable.hangar's map <paramref name="cmap"/> set (-1 none).</summary>
        public SpriteImage Frame(string sheet, int i, int cmap) => _frames.GetOrAdd((sheet, i, cmap), _ =>
        {
            var frames = _loaded.GetOrAdd(sheet, s => SpriteSheet.Load(_sheets, s));
            var maps = _main;
            if (cmap >= 0)
            {
                maps = _main.Clone();
                maps.Set(_hangar[cmap]);
            }
            return frames.Render(i, maps);
        });

        private string Words(string text) =>
            Regex.Replace(text, @"\{gs:(\d+)\}", m => _strings[int.Parse(m.Groups[1].Value)]);

        /// <summary>A picture painted with colortable.hangar's map <paramref name="cmap"/> set (-1 none).</summary>
        public ScreenRaster Raster(int cmap) => new((s, i) => Frame(s, i, s == StarbaseScreens.ShipyardSheet ? cmap : -1),
            f => _fonts[f], Words);

        /// <summary>Paints <paramref name="calls"/> over <paramref name="onto"/> with the colour map <paramref name="cmap"/>.</summary>
        public void Paint(ScreenRaster onto, IEnumerable<UqmDraw> calls, int cmap)
        {
            var painter = Raster(cmap);
            Array.Copy(onto.Rgba, painter.Rgba, onto.Rgba.Length);
            painter.Paint(calls);
            Array.Copy(painter.Rgba, onto.Rgba, onto.Rgba.Length);
        }
    }

    private static readonly Lazy<Pictures> Pics = new(() => TestContent.With(p => new Pictures(p)));

    // A recorded call as the port's canvas would have made it.
    private static UqmDraw Call(JsonElement e)
    {
        var a = e.EnumerateArray().ToArray();
        string kind = a[0].GetString()!;
        int I(int k) => a[k].GetInt32();
        Rgb C(int k) => new((byte)I(k), (byte)I(k + 1), (byte)I(k + 2));
        URect Clip(int k) => new(I(k), I(k + 1), I(k + 2), I(k + 3));
        return kind switch
        {
            "s" => new UqmDraw(UqmDrawKind.Stamp, I(3), I(4), Sheet: a[1].GetString()!, Frame: I(2), Clip: Clip(5)),
            "f" => new UqmDraw(UqmDrawKind.FilledStamp, I(3), I(4), Color: C(5), Sheet: a[1].GetString()!, Frame: I(2), Clip: Clip(8)),
            "r" => new UqmDraw(UqmDrawKind.Rect, I(1), I(2), I(3), I(4), C(5), Clip: Clip(8)),
            "o" => new UqmDraw(UqmDrawKind.Outline, I(1), I(2), I(3), I(4), C(5), Clip: Clip(8)),
            "c" => new UqmDraw(UqmDrawKind.Clear, I(1), I(2), I(3), I(4), C(5), Clip: Clip(8)),
            "p" => new UqmDraw(UqmDrawKind.Point, I(1), I(2), Color: C(3), Clip: Clip(6)),
            "t" => new UqmDraw(UqmDrawKind.Text, I(2), I(3), Color: C(7), Font: a[1].GetString()!, Align: (TextAlign)I(4),
                Count: I(5), Text: a[6].GetString()!, Effect: I(10), Clip: Clip(11)),
            _ => throw new InvalidDataException($"no drawing call {kind}"),
        };
    }

    [ContentFact]
    public void TheShipyardsFramesButThePowerLinesLookTheSameInEveryMap()
    {
        // The client paints a drawing's calls in the map it ends with: the shipyard's frames UQM draws before
        // the power lines set theirs (the hangar, as it opens) mustn't depend on the map set before.
        var pics = Pics.Value;
        int frames = FrameCount(StarbaseScreens.ShipyardSheet);
        for (int f = 0; f < frames; ++f)
        {
            if (f == StarbaseScreens.PowerLinesFrame)
                continue;
            var plain = pics.Frame(StarbaseScreens.ShipyardSheet, f, -1).Rgba;
            for (int map = 0; map < pics.HangarMaps; ++map)
                Assert.True(plain.SequenceEqual(pics.Frame(StarbaseScreens.ShipyardSheet, f, map).Rgba), $"frame {f} in map {map}");
        }
    }

    // A ship for the screens: a crew pod, a storage bay and a fuel tank, two thrusters, two jets.
    private static StarbaseShip TestStarbaseShip()
    {
        var modules = Enumerable.Repeat(ShipModules.EmptyModule, ShipModules.ModuleSlotCount).ToArray();
        (modules[1], modules[2], modules[3]) = (ShipModules.FuelTank, ShipModules.CrewPod, ShipModules.StorageBay);
        var drives = Enumerable.Repeat(ShipModules.EmptyDrive, ShipModules.DriveSlotCount).ToArray();
        (drives[5], drives[6]) = (ShipModules.FusionThruster, ShipModules.FusionThruster);
        var jets = Enumerable.Repeat(ShipModules.EmptyJet, ShipModules.JetSlotCount).ToArray();
        (jets[0], jets[6]) = (ShipModules.TurningJets, ShipModules.TurningJets);
        return new StarbaseShip(modules, drives, jets, 30, 2350, 1, 500, 350, "Ship", "Captain", 0, 0, 20, 3,
            Enumerable.Range(1, ShipModules.Modules).ToArray(), 50);
    }

    // The client's picture, painted from the content, of these drawings drawn one over another.
    private static ScreenRaster Painted(params StarbaseDrawing[] drawings)
    {
        var pics = Pics.Value;
        var picture = new StarbasePicture(Canvas(), Placeholder, FrameCount(StarbaseScreens.StarbaseSheet), pics.HangarMaps,
            s => SisState.Of(s, "", ""));
        var raster = pics.Raster(-1);
        foreach (var d in drawings)
            pics.Paint(raster, picture.Draw(new[] { d }, 0), picture.ColorMap);
        return raster;
    }

    private static StarbaseDrawing D(StarbaseShip ship, params ScreenOp[] ops) => new(ship, ops);

    private static ScreenOp Op(ScreenOpKind kind, int a = 0, int b = 0, int c = 0) => new(kind, a, b, c);

    public static IEnumerable<object[]> OthersChanges() => new[] { "units", "more minerals", "fewer minerals",
        "yard more minerals", "yard fewer minerals", "yard units", "crew price", "module price" }.Select(c => new object[] { c });

    [ContentTheory]
    [MemberData(nameof(OthersChanges))]
    public void WhatOthersChangeDrawnOverTheScreenLooksAsTheScreenDrawnAfresh(string change)
    {
        var was = TestStarbaseShip();
        const int U = ScreenOp.Undefined;
        var outfit = new[] { Op(ScreenOpKind.OutfitOpened, (int)was.Fuel), Op(ScreenOpKind.SisDisplay) };
        var yard = new[] { Op(ScreenOpKind.YardOpened), Op(ScreenOpKind.SisDisplay) };
        var choosing = outfit.Append(Op(ScreenOpKind.ClearRadar)).Append(Op(ScreenOpKind.ModuleStrings, ShipModules.CrewPod)).ToArray();
        var costs = was.ModuleCosts.ToArray();
        costs[ShipModules.CrewPod] += 7;
        // The screen before, the change as StarbaseMenu.Noticed draws it, and the ship after.
        var (screen, now, drawn) = change switch
        {
            "units" => (outfit, was with { ResourceUnits = 1234 }, new[] { Op(ScreenOpKind.Gauges, 0, 0, U) }),
            "more minerals" => (outfit, was with { MineralMass = 480 }, new[] { Op(ScreenOpKind.SisDisplay) }),
            "fewer minerals" => (outfit, was with { MineralMass = 20 }, new[] { Op(ScreenOpKind.SisDisplay) }),
            "yard more minerals" => (yard, was with { MineralMass = 480 }, new[] { Op(ScreenOpKind.SisDisplay), Op(ScreenOpKind.Blueprint) }),
            "yard fewer minerals" => (yard, was with { MineralMass = 20 }, new[] { Op(ScreenOpKind.SisDisplay), Op(ScreenOpKind.Blueprint) }),
            "yard units" => (yard, was with { ResourceUnits = 7 }, new[] { Op(ScreenOpKind.Gauges, 0, 0, U) }),
            "crew price" => (yard, was with { CrewCost = 5 }, new[] { Op(ScreenOpKind.MenuStrings, ScreenOp.PmCrew, ShipyardMenu.Crew) }),
            _ => (choosing, was with { ModuleCosts = costs }, new[] { Op(ScreenOpKind.ModuleStrings, ShipModules.CrewPod) }),
        };
        var over = Painted(D(was, screen), D(now, drawn));
        var fresh = Painted(D(now, screen));
        var diff = fresh.Differences(over);
        Assert.True(diff.Count == 0, $"{change}: differs at " + string.Join(", ", diff.Select(p => $"({p.X}, {p.Y}) fresh {Pixel(fresh, p)} over {Pixel(over, p)}")));
        Assert.NotEmpty(Painted(D(was, screen)).Differences(fresh));   // the change shows
    }

    [Fact]
    public void APictureAsksForItsScreenAFewTimesAndNeverForTheTalk()
    {
        var picture = new StarbasePicture(Canvas(), Placeholder, FrameCount(StarbaseScreens.StarbaseSheet), 3, s => SisState.Of(s, "", ""));
        Assert.False(picture.Ask(null));                    // not docked
        Assert.False(picture.Ask(StarbaseScreen.Talk));     // the communication screen shows
        for (int i = 0; i < StarbasePicture.MostAsks; ++i)
            Assert.True(picture.Ask(StarbaseScreen.Menu));
        Assert.False(picture.Ask(StarbaseScreen.Menu));     // a drawing that can't be painted isn't asked for forever
        Assert.True(picture.Ask(StarbaseScreen.Outfit));    // another screen: asked again
        picture.Draw(new[] { new StarbaseDrawing(null, new[] { new ScreenOp(ScreenOpKind.MenuOpened, 0) }) }, 0);
        Assert.True(picture.Drawn);
        Assert.False(picture.Ask(StarbaseScreen.Menu));     // drawn: nothing to ask
        picture.Reset();
        Assert.False(picture.Drawn);
        Assert.True(picture.Ask(StarbaseScreen.Menu));
        Assert.Empty(picture.Tick(10_000));                 // no picture: no clock drawing
    }

    [ContentFact]
    public void TheHangarsColourTableIsAsRecorded() =>
        Assert.Equal(Root.GetProperty("hangar_maps").GetInt32(), Pics.Value.HangarMaps);

    [ContentTheory]
    [MemberData(nameof(Scenarios))]
    public void EveryStepLooksAsUqmsScreenDoes(string name)
    {
        var sc = Scenario(name);
        var steps = sc.GetProperty("steps").EnumerateArray().Select(s => s.GetString()!).ToList();
        var drawn = sc.GetProperty("drawn");
        var cmap = sc.GetProperty("cmap");
        var pics = Pics.Value;
        var uqm = pics.Raster(-1);
        var mine = pics.Raster(-1);
        var port = new Replay(sc);
        for (int i = 0; i < steps.Count; ++i)
        {
            pics.Paint(uqm, drawn[i].EnumerateArray().Select(Call), cmap[i].GetInt32());
            pics.Paint(mine, port.Step(steps[i]), port.Screens.ColorMap);
            var diff = uqm.Differences(mine);
            if (diff.Count != 0 && Environment.GetEnvironmentVariable("CRATERIS_DUMP_SCREENS") is { Length: > 0 } dump)
            {
                Dump(Path.Combine(dump, $"{name}-{i}-uqm.ppm"), uqm);
                Dump(Path.Combine(dump, $"{name}-{i}-port.ppm"), mine);
            }
            Assert.True(diff.Count == 0, $"{Where(name, i, steps[i])}: the port's screen differs at "
                + string.Join(", ", diff.Select(p => $"({p.X}, {p.Y}) UQM {Pixel(uqm, p)} port {Pixel(mine, p)}")));
        }
    }

    // A picture to look at when a step differs (CRATERIS_DUMP_SCREENS names a folder).
    private static void Dump(string path, ScreenRaster r)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var f = File.Create(path);
        f.Write(System.Text.Encoding.ASCII.GetBytes($"P6 {ScreenRaster.Width} {ScreenRaster.Height} 255\n"));
        for (int i = 0; i < r.Rgba.Length; i += 4)
            f.Write(r.Rgba, i, 3);
    }

    private static string Pixel(ScreenRaster r, (int X, int Y) p)
    {
        int o = (p.Y * ScreenRaster.Width + p.X) * 4;
        return $"#{r.Rgba[o]:x2}{r.Rgba[o + 1]:x2}{r.Rgba[o + 2]:x2}";
    }
}
