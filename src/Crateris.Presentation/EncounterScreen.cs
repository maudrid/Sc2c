// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/encount.c: InitEncounter,
// DrawFadeText, UninitEncounter's tally; src/uqm/pickship.c:
// DrawArmadaPickShip with the salvage frame; src/uqm/util.c:
// WaitForAnyButton), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Systems;
using Crateris.Core.Uqm;
using Crateris.Core.World;

namespace Crateris.Presentation;

/// <summary>
/// UQM's encounter screen (InitEncounter) in the space window: the segue's
/// picture, the title ("ENCOUNTER IN DEEP SPACE", or "ENCOUNTER AT" the
/// cluster and the world), the battle's gravity well in the middle and the
/// group's ships round it, each its race's status icon, ten to a ring, an
/// infinite fleet's one ship sixty times (stage 3b-1 item 11). The menu
/// beside it (Hail, Attack, the host's Game menu) and the message line are
/// the status area's, which the port shows in words.
/// </summary>
public static class EncounterScreen
{
    /// <summary>SEGUE_PMAP_ANIM: the encounter screen's background.</summary>
    public const string SegueSheet = "graphics.segue";

    /// <summary>The most ships InitEncounter draws (an infinite fleet's one ship that many times).</summary>
    public const int MostShips = 60;

    private static readonly Rgb TitleColor = Rgb.From15(0x00, 0x00, 0x14), Black = Rgb.From15(0, 0, 0);

    // InitEncounter's display_pt: round the gravity well.
    private static readonly (short X, short Y)[] DisplayPoints =
    {
        (10, 51), (-10, 51), (33, 40), (-33, 40), (49, 18), (-49, 18), (52, -6), (-52, -6), (44, -27), (-44, -27),
    };

    /// <summary>
    /// InitEncounter's drawing in the space window: the window cleared, the
    /// segue, the title (<paramref name="cluster"/> null: deep space; else
    /// the cluster and <paramref name="world"/>, GLOBAL_SIS (PlanetName)),
    /// the gravity well (<paramref name="wellSheet"/>'s frame 0: planet[0],
    /// BuildBattle's centrepiece) and each ship's status icons
    /// (<paramref name="shipIcons"/>, one a ship, at most
    /// <see cref="MostShips"/>), frame 0.
    /// </summary>
    public static void Draw(UqmCanvas c, Func<int, string> gameString, string? cluster, string world, string wellSheet,
        IReadOnlyList<string> shipIcons)
    {
        var old = c.SetContext(c.Space);
        var clip = c.ClipRect;
        c.SetFont("tiny");
        c.SetBackground(Black);
        c.Clear();
        c.Stamp(SegueSheet, 0, 0, 0);
        int tx = (clip.W >> 1) + 1, ty = 10;
        c.SetFont("micro");
        c.SetForeground(TitleColor);
        if (cluster is null)
        {
            c.Text(tx, ty, TextAlign.Center, gameString(GameStrings.EncounterBase + 0));   // "ENCOUNTER IN"
            c.Text(tx, ty + 12, TextAlign.Center, gameString(GameStrings.EncounterBase + 1));   // "DEEP SPACE"
        }
        else
        {
            c.Text(tx, ty, TextAlign.Center, gameString(GameStrings.EncounterBase + 2));   // "ENCOUNTER AT"
            c.Text(tx, ty + 12, TextAlign.Center, cluster);
            c.Text(tx, ty + 24, TextAlign.Center, world);
        }
        // DrawSISMessage (NULL): the message line, outside the window (the status lines say where).
        int mx = clip.W >> 1, my = clip.H >> 1;
        c.Stamp(wellSheet, 0, mx, my);
        for (int i = 0; i < shipIcons.Count && i < MostShips; ++i)
        {
            var (x, y) = ShipPoint(i);
            var r = c.FrameRect(shipIcons[i], 0);
            c.Stamp(shipIcons[i], 0, x + mx - (r.W >> 1), y + my - (r.H >> 1));
        }
        c.SetContext(old);
    }

    /// <summary>
    /// The gravity well's sheet for a battle's planet (a data_index:
    /// <see cref="MeetingView.Well"/>): load_gravity_well's big view of its
    /// planet type, or a shielded world's ("slaveshield") whatever its type.
    /// </summary>
    public static string WellSheet(byte battlePlanet, PlanetTypes planets) =>
        (WorldTypes.IsShielded(battlePlanet) ? planets.ShieldedBattleSprites
            : planets[WorldTypes.PlanetType(battlePlanet)].BattleSprites)[0];

    /// <summary>
    /// Where the title says the meeting is: in deep space no cluster (null);
    /// in a system its cluster, and GLOBAL_SIS (PlanetName) as generateName
    /// sets it: the planet's own name (at a world, the planet whose moon it
    /// is: a moon's name is never used), the planet named in a system
    /// (<see cref="MeetingView.Named"/>), else nothing.
    /// </summary>
    public static (string? Cluster, string World) Place(MeetingView meeting, StarMap map, Names names)
    {
        if (meeting.Star is not int s)
            return (null, "");
        var star = map.Stars[s];
        int? planet = meeting.Planet ?? meeting.Named;
        return (names.Cluster(star), planet is int p ? names.PlanetName(star, p) : "");
    }

    /// <summary>
    /// InitEncounter's place for the <paramref name="i"/>th ship, from the
    /// window's middle: the ten display points, and past them the same
    /// points 18 pixels further out for each ten.
    /// </summary>
    public static (int X, int Y) ShipPoint(int i)
    {
        var (x, y) = DisplayPoints[i % DisplayPoints.Length];
        if (i < DisplayPoints.Length)
            return (x, y);
        int radius = UqmMath.SquareRoot((uint)(x * x + y * y)) + i / DisplayPoints.Length * 18;
        int angle = UqmMath.ArcTan(x, y);
        return (UqmMath.Cosine(angle, radius), UqmMath.Sine(angle, radius));
    }
}

/// <summary>
/// UQM's battle-won screen (UninitEncounter after a battle won, stage 3b-1
/// item 11), in the space window as UQM draws it there, on the client's
/// clock (UQM's ticks, ONE_SECOND 840): the salvage frame over the pick
/// frame with the flagship's name; the first enemy ship's picture, its
/// race's name and "BATTLE GROUP", "Enemy Ships Destroyed" fading in;
/// then each ship destroyed, its bounty added to the count (each
/// ship's: RACE_SHIP_COST / 8) and its icon flashing red and white into
/// its place, five to a row; three seconds or a key; the words fading
/// out, and the player's salvage (their share: the owner's rule, salvage
/// split between the players in the battle when it's won) as "N RU" with
/// "Debris Scavenged" fading in, two seconds or a key, and out. A key held
/// cuts each fade to its last colour and the ships' flashes short; Escape
/// held after a wait leaves the rest out. The status area's parts (the
/// frame, the resource units counting up) are the status lines'.
/// </summary>
public sealed class BattleWonScreen
{
    /// <summary>SC2_PICK_PMAP_ANIM: the pick frame (0) and the salvage frame over it (1).</summary>
    public const string PickSheet = "graphics.pickship";

    private const long OneSecond = 840;
    private const int MostShown = 5;   // MAX_DEAD_DISPLAYED

    private static Rgb C(int r, int g, int b) => Rgb.From15((byte)r, (byte)g, (byte)b);

    private static readonly Rgb Black = C(0, 0, 0), NameColor = C(0x12, 0x12, 0x12), RaceColor = C(0x08, 0x08, 0x08),
        CountColor = C(0x00, 0x00, 0x18), FadedOut = C(0x0A, 0x0A, 0x0A);

    // DrawFadeText's fade_cycle.
    private static readonly Rgb[] Fades =
        { C(0x0A, 0x0A, 0x0A), C(0x09, 0x09, 0x09), C(0x08, 0x08, 0x08), C(0x06, 0x06, 0x06), C(0x05, 0x05, 0x05),
          C(0x04, 0x04, 0x04), C(0x03, 0x03, 0x03) };

    // UninitEncounter's fade_ship_cycle.
    private static readonly Rgb[] ShipFades =
        { C(0x07, 0x00, 0x00), C(0x0F, 0x00, 0x00), C(0x17, 0x00, 0x00), C(0x1F, 0x0A, 0x0A), C(0x1F, 0x14, 0x14),
          C(0x1F, 0x1F, 0x1F), C(0x1F, 0x14, 0x14), C(0x1F, 0x0A, 0x0A), C(0x1B, 0x00, 0x00), C(0x17, 0x00, 0x00) };

    private readonly UqmCanvas _c;
    private readonly Func<int, string> _gameString;
    private readonly string _shipName, _raceName, _icons;
    private readonly int _destroyed;
    private readonly uint _bounty, _salvage;
    private readonly IEnumerator<long> _run;
    private long _now, _until = long.MinValue;
    private bool _held, _escape;

    /// <param name="canvas">Where it draws (its calls in <see cref="UqmCanvas.Drawn"/>).</param>
    /// <param name="gameString">The game's strings by number.</param>
    /// <param name="shipName">The flagship's name (GLOBAL_SIS (ShipName)), as the pick frame shows it.</param>
    /// <param name="raceName">The race's name (its race strings' first), shown in capitals.</param>
    /// <param name="icons">The race ship's status icons (frame 1 the picture, 0 the icon).</param>
    /// <param name="destroyed">How many enemy ships were destroyed.</param>
    /// <param name="bounty">Each ship's bounty.</param>
    /// <param name="salvage">The player's salvage, the "N RU" scavenged.</param>
    /// <param name="now">The clock as it starts.</param>
    public BattleWonScreen(UqmCanvas canvas, Func<int, string> gameString, string shipName, string raceName, string icons,
        int destroyed, uint bounty, uint salvage, long now)
    {
        _c = canvas;
        _gameString = gameString;
        _shipName = shipName;
        _raceName = raceName;
        _icons = icons;
        _destroyed = destroyed;
        _bounty = bounty;
        _salvage = salvage;
        _now = now;
        _run = Run().GetEnumerator();
    }

    /// <summary>Whether it has run to its end.</summary>
    public bool Done { get; private set; }

    /// <summary>
    /// Time passes to <paramref name="now"/>, a key held or not
    /// (<paramref name="held"/>: AnyButtonPress) and Escape among them
    /// (<paramref name="escape"/>): whatever is due is drawn. True once
    /// it's over.
    /// </summary>
    public bool Advance(long now, bool held, bool escape)
    {
        _held = held;
        _escape = escape;
        _now = Math.Max(_now, now);
        while (!Done && _now >= _until)
        {
            if (!_run.MoveNext())
                Done = true;
            else
                _until = _run.Current;
        }
        return Done;
    }

    // The salvage frame over the pick frame (DrawArmadaPickShip (TRUE, &scavenge_r)): the rectangle the tally is in.
    private URect PickFrames()
    {
        var clip = _c.ClipRect;
        var pick = _c.FrameRect(PickSheet, 0);
        int px = (clip.W >> 1) - (pick.W >> 1), py = (clip.H >> 1) - (pick.H >> 1);
        var salvage = _c.FrameRect(PickSheet, 1);
        int sx = (clip.W >> 1) - (salvage.W >> 1), sy = py - (salvage.H >> 1);
        _c.Stamp(PickSheet, 1, sx - salvage.X, sy - salvage.Y);   // SetFrameHot (0, 0): its corner at the origin
        py = sy + salvage.H;
        _c.Stamp(PickSheet, 0, px - pick.X, py - pick.Y);
        _c.SetForeground(NameColor);
        _c.SetFont("starcon");
        _c.Text(px + (pick.W >> 1), py + pick.H - 5, TextAlign.Center, _shipName);
        return new URect(px, sy, salvage.W, salvage.H);
    }

    // The coroutine: each value the tick it waits until.
    private IEnumerable<long> Run()
    {
        var old = _c.SetContext(_c.Space);
        var scav = PickFrames();
        int shipX = 0, shipY = 0, shown = 1;
        uint recycled = 0;
        bool sleepy = true;
        string str1 = "", str2 = "";
        var r = new URect(0, 0, 0, 0);
        for (int k = 1; k <= _destroyed; ++k)
        {
            if (k == 1)
            {
                _c.Stamp(_icons, 1, scav.X + 32, scav.Y + 56);
                _c.SetForeground(RaceColor);
                _c.SetFont("tiny");
                _c.Text(scav.X + 100, scav.Y + 68, TextAlign.Center, Upper(_raceName));
                _c.Text(scav.X + 100, scav.Y + 74, TextAlign.Center, _gameString(GameStrings.EncounterBase + 3));   // "BATTLE GROUP"
                _c.SetFont("micro");
                str1 = _gameString(GameStrings.EncounterBase + 4);   // "Enemy Ships"
                str2 = _gameString(GameStrings.EncounterBase + 5);   // "Destroyed"
                foreach (long t in FadeText(str1, str2, true, scav))
                    yield return t;
            }
            r = new URect(scav.X + scav.W - (10 + 34), scav.Y + 9, 34, 22);
            _c.SetForeground(Black);
            _c.FillRect(r);
            recycled += _bounty;
            _c.SetForeground(CountColor);
            _c.Text(r.X + r.W - 1, r.Y + 14, TextAlign.Right, recycled.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if ((shown++ - 1) % MostShown != 0)
                shipX += 17;
            else
            {
                _c.SetForeground(Black);
                r = new URect(scav.X + 10, r.Y, 104, r.H);
                _c.FillRect(r);
                shipX = r.X + 2;
                shipY = scav.Y + 12;
            }
            if (sleepy)
            {
                long time = _now;
                for (int j = 0; j < ShipFades.Length; ++j)
                {
                    sleepy = !_held;
                    if (!sleepy)
                        break;
                    _c.SetForeground(ShipFades[j]);
                    _c.FilledStamp(_icons, 0, shipX, shipY);
                    yield return time + OneSecond / 15;
                    time = _now;
                }
            }
            _c.Stamp(_icons, 0, shipX, shipY);
        }
        if (_destroyed > 0)
        {
            foreach (long t in WaitForAnyButton(OneSecond * 3))
                yield return t;
            if (!_escape)
            {
                foreach (long t in FadeText(str1, str2, false, scav))
                    yield return t;
                if (!_escape)
                {
                    _c.SetForeground(Black);
                    r = new URect(scav.X + 10, r.Y, 132, r.H);
                    _c.FillRect(r);
                    _c.SetForeground(CountColor);
                    _c.Text(r.X + (r.W >> 1), r.Y + 14, TextAlign.Center,
                        $"{_salvage.ToString(System.Globalization.CultureInfo.InvariantCulture)} {_gameString(GameStrings.StatusBase + 1)}");   // "RU"
                    str1 = _gameString(GameStrings.EncounterBase + 6);   // "Debris"
                    str2 = _gameString(GameStrings.EncounterBase + 7);   // "Scavenged"
                    foreach (long t in FadeText(str1, str2, true, scav))
                        yield return t;
                    foreach (long t in WaitForAnyButton(OneSecond * 2))
                        yield return t;
                    if (!_escape)
                        foreach (long t in FadeText(str1, str2, false, scav))
                            yield return t;
                }
            }
        }
        _c.SetContext(old);
    }

    // strupr: ASCII letters only, as UQM's (its own XXX says so).
    private static string Upper(string s) =>
        new(s.Select(ch => ch is >= 'a' and <= 'z' ? (char)(ch - ('a' - 'A')) : ch).ToArray());

    // DrawFadeText: the two lines in the fade's colours, a twentieth of a second each, a key cutting it to its end.
    private IEnumerable<long> FadeText(string s1, string s2, bool fadeIn, URect rect)
    {
        int x = rect.X + 100, y = rect.Y + 45;
        long time = _now;
        if (fadeIn)
        {
            for (int i = 0; i < Fades.Length; ++i)
            {
                if (_held)
                    i = Fades.Length - 1;
                Draw(Fades[i]);
                yield return time + OneSecond / 20;
                time = _now;
            }
        }
        else
        {
            for (int i = Fades.Length - 1; i >= 0; --i)
            {
                if (_held)
                    i = 0;
                Draw(Fades[i]);
                yield return time + OneSecond / 20;
                time = _now;
            }
            Draw(FadedOut);
        }

        void Draw(Rgb color)
        {
            _c.SetForeground(color);
            _c.Text(x, y, TextAlign.Center, s1);
            _c.Text(x, y + 11, TextAlign.Center, s2);
        }
    }

    // WaitForAnyButton (TRUE, duration): a key held first let go, then one pressed, or the time out.
    private IEnumerable<long> WaitForAnyButton(long duration)
    {
        long timeOut = _now + duration;
        while (_held && _now < timeOut)
            yield return _now + OneSecond / 40;
        while (!_held && _now < timeOut)
            yield return _now + OneSecond / 40;
    }
}
