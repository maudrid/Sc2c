// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;

namespace Crateris.Core.Data;

/// <summary>
/// One of a race's animations (ANIMATION_DESC, commanim.h): its frames or
/// colour maps from <see cref="StartIndex"/>, how it plays
/// (<see cref="Flags"/>), and its delays in UQM's time units (840 a
/// second): between frames base plus up to random more, and likewise
/// before it starts again. <see cref="BlockMask"/> has a bit for each
/// ambient animation that can't run with it.
/// </summary>
public sealed record CommAnimation(int StartIndex, int NumFrames, CommAnimationFlags Flags,
    int BaseFrameRate, int RandomFrameRate, int BaseRestartRate, int RandomRestartRate, uint BlockMask);

/// <summary>AnimFlags (commanim.h).</summary>
[Flags]
public enum CommAnimationFlags
{
    None = 0,
    Random = 1 << 0,
    Circular = 1 << 1,
    YoYo = 1 << 2,
    WaitTalking = 1 << 3,
    /// <summary>PAUSE_TALKING; in an ambient animation, COLORXFORM_ANIM (it plays colour maps).</summary>
    PauseTalking = 1 << 4,
    /// <summary>TALK_INTRO; in an ambient animation, ONE_SHOT_ANIM.</summary>
    TalkIntro = 1 << 5,
    TalkDone = 1 << 6,
    Disabled = 1 << 7,
}

/// <summary>
/// A race's description for its conversations (LOCDATA, globdata.h), as
/// the compiler builds it, before the script's set-up changes it: its
/// content's resource ids, where and how its text goes, and its
/// animations.
/// </summary>
public sealed record CommDescription(
    string Frames, string Font, string ColorMap, string Song, string AltSong, bool UseAltSong, string Phrases,
    Rgba TextFore, Rgba TextBack, int TextBaselineX, int TextBaselineY, int TextWidth, int TextAlign, int TextValign,
    bool NumberSpeech, IReadOnlyList<CommAnimation> Ambient, CommAnimation Transition, CommAnimation Talk);

/// <summary>A colour with its alpha (UQM's Color).</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A);

/// <summary>
/// A conversation script (src/uqm/comm/NAME/): its init function, its race
/// description and its phrase ids in strings.h's order.
/// </summary>
public sealed class ConversationScript
{
    private readonly Dictionary<string, int> _phraseNumber;

    internal ConversationScript(string name, string init, CommDescription description, IReadOnlyList<string> phrases)
    {
        Name = name;
        Init = init;
        Description = description;
        Phrases = phrases;
        _phraseNumber = phrases.Select((p, i) => (p, i)).ToDictionary(x => x.p, x => x.i + 1);
    }

    /// <summary>The script's folder name (comandr, starbas, ilwrath...).</summary>
    public string Name { get; }

    public string Init { get; }

    public CommDescription Description { get; }

    /// <summary>The phrase ids; phrase n (1 and up) is <c>Phrases[n - 1]</c> and entry n - 1 of the conversation.</summary>
    public IReadOnlyList<string> Phrases { get; }

    /// <summary>A phrase's number, as the script's enum gives it (NULL_PHRASE is 0).</summary>
    public int PhraseNumber(string phrase) =>
        _phraseNumber.TryGetValue(phrase, out int n) ? n : throw new KeyNotFoundException($"{Name} has no phrase {phrase}");
}

/// <summary>
/// Every conversation script, and which runs for each conversation id
/// (init_race, commglue.c), from data/conversations.json.
/// </summary>
public sealed class ConversationScripts
{
    private readonly Dictionary<string, ConversationScript> _scripts;
    private readonly Dictionary<string, IReadOnlyList<ConversationScript>> _byConversation;

    private ConversationScripts(Dictionary<string, ConversationScript> scripts,
        Dictionary<string, IReadOnlyList<ConversationScript>> byConversation)
    {
        _scripts = scripts;
        _byConversation = byConversation;
    }

    public IReadOnlyCollection<ConversationScript> All => _scripts.Values;

    public ConversationScript this[string script] =>
        _scripts.TryGetValue(script, out var s) ? s : throw new KeyNotFoundException($"no conversation script {script}");

    /// <summary>
    /// The scripts init_race can run for a conversation id: one, or two
    /// where it chooses by the story (the commander, the Spathi).
    /// </summary>
    public IReadOnlyList<ConversationScript> For(string conversation) =>
        _byConversation.TryGetValue(conversation, out var s) ? s : throw new KeyNotFoundException($"no conversation {conversation}");

    public static ConversationScripts Load(string dataDirectory)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(dataDirectory, "conversations.json")));
        var root = doc.RootElement;
        var scripts = new Dictionary<string, ConversationScript>();
        foreach (var s in root.GetProperty("scripts").EnumerateArray())
        {
            var d = s.GetProperty("description");
            static CommAnimation Anim(JsonElement a)
            {
                var v = a.EnumerateArray().Select(x => x.GetInt64()).ToArray();
                if (v.Length != 8)
                    throw new InvalidDataException("conversations.json: an animation needs 8 numbers");
                return new CommAnimation((int)v[0], (int)v[1], (CommAnimationFlags)v[2], (int)v[3], (int)v[4], (int)v[5], (int)v[6], (uint)v[7]);
            }
            static Rgba Color(JsonElement c)
            {
                var v = c.EnumerateArray().Select(x => x.GetByte()).ToArray();
                return new Rgba(v[0], v[1], v[2], v[3]);
            }
            var baseline = d.GetProperty("text_baseline").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            var description = new CommDescription(
                d.GetProperty("frames").GetString()!, d.GetProperty("font").GetString()!,
                d.GetProperty("color_map").GetString()!, d.GetProperty("song").GetString()!,
                d.GetProperty("alt_song").GetString()!, (d.GetProperty("song_flags").GetInt32() & 1) != 0,
                d.GetProperty("phrases").GetString()!,
                Color(d.GetProperty("text_fore")), Color(d.GetProperty("text_back")), baseline[0], baseline[1],
                d.GetProperty("text_width").GetInt32(), d.GetProperty("text_align").GetInt32(),
                d.GetProperty("text_valign").GetInt32(), d.GetProperty("number_speech").GetBoolean(),
                d.GetProperty("ambient").EnumerateArray().Select(Anim).ToList(),
                Anim(d.GetProperty("transition")), Anim(d.GetProperty("talk")));
            string name = s.GetProperty("script").GetString()!;
            scripts.Add(name, new ConversationScript(name, s.GetProperty("init").GetString()!, description,
                s.GetProperty("phrases").EnumerateArray().Select(p => p.GetString()!).ToList()));
        }
        var byConversation = root.GetProperty("conversations").EnumerateArray().ToDictionary(
            c => c.GetProperty("conversation").GetString()!,
            c => (IReadOnlyList<ConversationScript>)c.GetProperty("scripts").EnumerateArray()
                .Select(n => scripts.TryGetValue(n.GetString()!, out var sc) ? sc
                    : throw new InvalidDataException($"conversations.json: no script {n.GetString()}")).ToList());
        return new ConversationScripts(scripts, byConversation);
    }
}
