// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Data;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The alien's animations on the communication screen (docs/plans/stage-3.md,
/// item 11) against UQM's own commanim.c, every race's timeline
/// (tests/golden/commanim.json).
/// </summary>
[Trait("Topic", "drawing")]
public class AlienAnimationTests
{
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    private static readonly JsonElement Recorded = Golden.Load("commanim").GetProperty("races");

    public static IEnumerable<object[]> Races() =>
        Recorded.EnumerateArray().Select(r => new object[] { r.GetProperty("script").GetString()! });

    [Theory]
    [MemberData(nameof(Races))]
    public void EveryRacesAnimationsAreUqms(string script)
    {
        var race = Recorded.EnumerateArray().Single(r => r.GetProperty("script").GetString() == script);
        var d = Scripts[script].Description;
        var anim = new AlienAnimation(d.Ambient, d.Talk, d.Transition, race.GetProperty("seed").GetUInt32());
        var happened = race.GetProperty("happened").EnumerateArray().ToDictionary(h => h[0].GetInt32());
        var steps = race.GetProperty("steps").EnumerateArray().Select(s => s.GetString()!).ToList();
        int talk = 0, transit = 0;
        for (int k = 0; k < steps.Count; ++k)
        {
            var parts = steps[k].Split(':');
            if (parts.Length > 1)
            {
                string ev = parts[1];
                if (ev == "talk")
                    anim.StartTalking();
                else if (ev == "stop")
                    anim.StopTalking();
                else if (ev == "pause")
                    anim.PauseTalking(true);
                else if (ev == "unpause")
                    anim.PauseTalking(false);
                else
                {
                    var f = ev.Split('=');
                    anim.SetAmbientFlags(int.Parse(f[1]), (CommAnimationFlags)int.Parse(f[2]));
                }
            }
            var (frames, fades) = anim.Step(int.Parse(parts[0]));
            int[] wantFrames = Array.Empty<int>();
            (int, int)[] wantFades = Array.Empty<(int, int)>();
            if (happened.TryGetValue(k, out var h))
            {
                wantFrames = h[1].Ints();
                wantFades = h[2].EnumerateArray().Select(x => (x[0].GetInt32(), x[1].GetInt32())).ToArray();
                (talk, transit) = (h[3].GetInt32(), h[4].GetInt32());
            }
            string where = $"{script} step {k} ({steps[k]})";
            Assert.True(wantFrames.SequenceEqual(frames), $"{where}: frames [{string.Join(", ", frames)}], UQM [{string.Join(", ", wantFrames)}]");
            Assert.True(wantFades.SequenceEqual(fades.Select(x => (x.Map, x.Time))), $"{where}: fades");
            Assert.True((talk, transit) == ((int)anim.TalkFlags, (int)anim.TransitionFlags),
                $"{where}: flags {(int)anim.TalkFlags}, {(int)anim.TransitionFlags}; UQM {talk}, {transit}");
        }
    }
}
