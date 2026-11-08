// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Systems;
using Crateris.Presentation;

namespace Crateris.Core.Tests;

/// <summary>
/// The talk's text on the communication screen (docs/plans/stage-3.md,
/// item 11) against UQM's own comm.c and trackplayer.c with its fonts
/// (tests/golden/commtext.json): each race's subtitles paged and laid out,
/// replies, the reply chosen and the summary.
/// </summary>
[Trait("Topic", "drawing")]
public class CommTextTests
{
    private static readonly ConversationScripts Scripts = ConversationScripts.Load(Path.Combine(AppContext.BaseDirectory, "data"));
    private static readonly JsonElement Recorded = Golden.Load("commtext").GetProperty("races");

    public static IEnumerable<object[]> Races() =>
        Recorded.EnumerateArray().Select(r => new object[] { r.GetProperty("script").GetString()! });

    private static byte[] UntilNul(ReadOnlySpan<byte> raw)
    {
        int n = raw.IndexOf((byte)0);
        return (n < 0 ? raw : raw[..n]).ToArray();
    }

    private static string Hex(string s) => Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(s)).ToLowerInvariant();

    // A drawing call as commtext.c writes it.
    private static string Json(CommTextDraw d) => d.Kind switch
    {
        CommTextKind.Rectangle => new JsonArray("r", d.X, d.Y, d.Count, d.Offset, d.Color.R, d.Color.G, d.Color.B).ToJsonString(),
        CommTextKind.Stamp => new JsonArray("s", d.Count, d.X, d.Y).ToJsonString(),
        _ => new JsonArray(new JsonNode?[]
            {
                d.Kind == CommTextKind.Traced ? "traced" : "t", d.X, d.Y, (int)d.Align, d.Count,
                d.Source switch
                {
                    -1 => Hex(CommText.Bullet),
                    -2 => Hex(CommText.MiddleDot + "MORE" + CommText.MiddleDot),
                    -3 => Hex("FEEDBACK"),   // commtext.c's placeholder for the game string
                    var s => s,
                },
                d.Offset, d.Color.R, d.Color.G, d.Color.B,
            }.Concat(d.Kind == CommTextKind.Traced ? new JsonNode?[] { d.Trace.R, d.Trace.G, d.Trace.B } : Array.Empty<JsonNode?>())
            .ToArray()).ToJsonString(),
    };

    // UQM's drawing, as the port gives it: the subtitle cache's stamp left
    // out, and MORE's count all of it (DoConvSummary never sets its TEXT's
    // CharCount: whatever was on the stack).
    private static List<string> Want(JsonElement drawn) =>
        drawn.EnumerateArray().Where(e => !(e[0].GetString() == "s" && e[1].GetInt32() == 0))
            .Select(e =>
            {
                var node = JsonNode.Parse(e.GetRawText())!.AsArray();
                if (node[0]!.GetValue<string>() == "t" && node[5] is JsonValue v && v.TryGetValue<string>(out var src)
                        && src == Hex(CommText.MiddleDot + "MORE" + CommText.MiddleDot))
                    node[4] = -1;
                return node.ToJsonString();
            }).ToList();

    [ContentTheory]
    [MemberData(nameof(Races))]
    public void EveryRacesTextIsLaidOutAsUqmDoes(string script)
    {
        var race = Recorded.EnumerateArray().Single(r => r.GetProperty("script").GetString() == script);
        var d = Scripts[script].Description;
        TestContent.With(package =>
        {
            var font = UqmFont.Load(package, d.Font);
            var player = UqmFont.Load(package, "font.player");
            var tiny = UqmFont.Load(package, "font.tiny");
            var lines = Conversation.Load(package, d.Phrases).Entries.Select(e => UntilNul(e.Raw)).ToList();
            foreach (var line in race.GetProperty("lines").EnumerateArray())
            {
                int phrase = line.GetProperty("phrase").GetInt32();
                var pages = CommText.SplitPages(lines[phrase - 1]);
                var want = line.GetProperty("pages").EnumerateArray().Select(p => (p[0].GetString()!, p[1].GetInt32())).ToList();
                Assert.Equal(want, pages.Select(p => (Convert.ToHexString(SHA256.HashData(p.Page)).ToLowerInvariant()[..16], p.Millis)).ToList());
                var drawn = line.GetProperty("drawn").EnumerateArray().ToList();
                for (int i = 0; i < pages.Count; ++i)
                {
                    var got = CommText.Subtitle(font, pages[i].Page, d.TextBaselineX, d.TextBaselineY, (TextAlign)d.TextAlign,
                        d.TextValign, d.TextWidth, new Rgb(d.TextFore.R, d.TextFore.G, d.TextFore.B),
                        new Rgb(d.TextBack.R, d.TextBack.G, d.TextBack.B)).Select(Json).ToList();
                    Assert.True(Want(drawn[i]).SequenceEqual(got),
                        $"{script} phrase {phrase} page {i}:\n UQM {string.Join(" ", Want(drawn[i]))}\n port {string.Join(" ", got)}");
                }
            }
            foreach (var reply in race.GetProperty("replies").EnumerateArray())
            {
                var texts = reply.GetProperty("phrases").Ints().Select(p => lines[p - 1]).ToList();
                var got = CommText.Replies(player, texts, reply.GetProperty("top").GetInt32(), reply.GetProperty("cur").GetInt32())
                    .Select(Json).ToList();
                var want = Want(reply.GetProperty("drawn"));
                Assert.True(want.SequenceEqual(got), $"{script} replies:\n UQM {string.Join(" ", want)}\n port {string.Join(" ", got)}");
            }
            foreach (var fb in race.GetProperty("feedback").EnumerateArray())
            {
                var got = CommText.FeedbackPhrase(player, lines[fb.GetProperty("phrase").GetInt32() - 1]).Select(Json).ToList();
                var want = Want(fb.GetProperty("drawn"));
                Assert.True(want.SequenceEqual(got), $"{script} feedback:\n UQM {string.Join(" ", want)}\n port {string.Join(" ", got)}");
            }
            Assert.Single(CommText.FeedbackPhrase(player, Array.Empty<byte>()));
            var subs = race.GetProperty("summary_phrases").Ints().Select(p => lines[p - 1]).ToList();
            var summary = CommText.Summary(tiny, subs);
            var wantPages = race.GetProperty("summary").EnumerateArray().ToList();
            Assert.Equal(wantPages.Count, summary.Count);
            for (int i = 0; i < summary.Count; ++i)
            {
                var got = summary[i].Select(Json).ToList();
                var want = Want(wantPages[i]);
                Assert.True(want.SequenceEqual(got), $"{script} summary page {i}:\n UQM {string.Join(" ", want)}\n port {string.Join(" ", got)}");
            }
            return 0;
        });
    }
}
