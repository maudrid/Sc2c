// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/commglue.c, comm.c's
// DoResponsePhrase, SelectResponse and HailAlien, sis.c's DeltaSISGauges),
// Copyright Paul Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and
// the UQM contributors, under the GNU GPL 2 or later.

using System.Text;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Story;
using Crateris.Core.Uqm;

namespace Crateris.Core.Comm;

/// <summary>
/// One conversation, run on the host as UQM runs it: the script's init
/// function, then its intro (init_encounter_func), which queues the
/// alien's lines and offers replies; each reply chosen calls its function,
/// which queues more and offers more; a function that offers none ends the
/// talk, and the post-encounter and clean-up functions run. Everything it
/// does is in <see cref="Events"/>, as the recording program records it.
/// <para>
/// The script reads and writes the story through the talk: the shared
/// story state, and for the fields that aren't shared (decision 11) the
/// talk's own values (<see cref="OwnFields"/>: where the talk is and how it
/// ends, and the starter's own fields, such as whether the allied commander
/// has greeted them). It changes the starter's ship (<see cref="Ship"/>,
/// decision 4). Its random numbers come from a generator of its own,
/// seeded per talk (decision 12). Lines are as UQM gives them without
/// voices (usingSpeech false).
/// </para>
/// </summary>
public sealed class Talk
{
    /// <summary>MAX_RESPONSES (comm.c): more replies would overrun UQM's list.</summary>
    public const int MaxReplies = 8;

    /// <summary>NPCPhrase's special indexes (commglue.h).</summary>
    public const int GlobalPlayerName = -1000000, GlobalShipName = -999999, GlobalAllianceName = -999998;

    /// <summary>UNDEFINED_DELTA (sis.h): DeltaSISGauges redraws without a change.</summary>
    public const int UndefinedDelta = 0x7FFF;

    /// <summary>ONE_SECOND: UQM's clock ticks a second.</summary>
    public const int OneSecond = 840;

    /// <summary>AlienTalkSegue's "all of them" ((COUNT)~0).</summary>
    public const int WaitAll = 0xFFFF;

    private readonly CommScript _script;
    private readonly byte[][] _phrases;
    private readonly int _colorMapCount;
    private readonly StoryState _story;
    private readonly Dictionary<string, int> _own;
    private readonly TfbRandom _random;
    private readonly List<TalkEvent> _events = new();
    private readonly List<(int Ref, Action<int> Func, bool Shared)> _offered = new();
    private byte[] _shared = Array.Empty<byte>();
    private bool _screenLoaded, _instantVictory, _defeated;
    private int _lastMap = -1;
    private CommAnimationFlags[] _lastAmbient = Array.Empty<CommAnimationFlags>();
    private (int, uint, int, uint, int[])? _lastShip;

    /// <param name="script">The script, new for this talk.</param>
    /// <param name="description">Its race's description (data/conversations.json).</param>
    /// <param name="phrases">Its lines (the content's CONVERSATION resource), copied for this talk.</param>
    /// <param name="colorMapCount">How many colour maps its colour table has.</param>
    /// <param name="story">The group's story state.</param>
    /// <param name="ownFields">The fields that aren't shared, as they stand for this talk (0 if not given); changed as the talk writes them.</param>
    /// <param name="ship">The starter's ship.</param>
    /// <param name="seed">The scripts' random seed for this talk.</param>
    public Talk(CommScript script, CommDescription description, Conversation phrases, int colorMapCount,
        StoryState story, Dictionary<string, int> ownFields, ITalkShip ship, uint seed)
    {
        _script = script;
        Description = description;
        _phrases = phrases.Entries.Select(e => e.Raw.Length == 0 ? Array.Empty<byte>() : UntilNul(e.Raw)).ToArray();
        _clips = phrases.Entries.Select(e => e.Clip).ToArray();
        _colorMapCount = colorMapCount > 0 ? colorMapCount : throw new ArgumentOutOfRangeException(nameof(colorMapCount));
        _story = story;
        _own = ownFields;
        Ship = ship;
        _random = new TfbRandom(seed);
        Ambient = description.Ambient.Select(a => a.Flags).ToArray();
        AltSong = description.AltSong;
        UseAltSong = description.UseAltSong;
        foreach (var name in _own.Keys)
            if (!StoryState.NotShared.ContainsKey(name))
                throw new ArgumentException($"{name} is shared story state, not the talk's own", nameof(ownFields));
    }

    private readonly string?[] _clips;

    private static byte[] UntilNul(ReadOnlySpan<byte> raw)
    {
        int n = raw.IndexOf((byte)0);
        return (n < 0 ? raw : raw[..n]).ToArray();
    }

    public CommDescription Description { get; }

    public ITalkShip Ship { get; }

    /// <summary>Everything the talk has done so far.</summary>
    public IReadOnlyList<TalkEvent> Events => _events;

    /// <summary>The fields that aren't shared, as the talk has left them.</summary>
    public IReadOnlyDictionary<string, int> OwnFields => _own;

    /// <summary>The replies offered now (empty before the start and once ended).</summary>
    public IReadOnlyList<TalkReply> Replies => _offered.Select(Reply).ToList();

    public bool Started { get; private set; }

    public bool Ended { get; private set; }

    // ------------------------------------------------------------ the screen

    /// <summary>The colour map in use (CommData.AlienColorMap's index).</summary>
    public int ColorMap { get; private set; }

    /// <summary>Each ambient animation's flags (CommData.AlienAmbientArray[i].AnimFlags), which a script may change.</summary>
    public CommAnimationFlags[] Ambient { get; }

    /// <summary>The init function's song choice (AlienAltSongRes, LDASF_USE_ALTERNATE).</summary>
    public string AltSong { get; set; }

    public bool UseAltSong { get; set; }

    /// <summary>SetAbsColorMapIndex (CommData.AlienColorMap, index): the index wraps at the table's size.</summary>
    public void SetColorMap(int index) => ColorMap = index % _colorMapCount;

    /// <summary>XFormColorMap (GetColorMapAddress (SetAbsColorMapIndex (CommData.AlienColorMap, index)), time).</summary>
    public void XFormColorMap(int index, int time)
    {
        Check();
        _events.Add(new ColorTransform(index % _colorMapCount, time));
    }

    /// <summary>AlienTalkSegue: the lines play up to this track before the script goes on.</summary>
    public void AlienTalkSegue(int waitTrack)
    {
        Check();
        _events.Add(new TalkWait(waitTrack));
    }

    // ------------------------------------------------------------ the flow

    /// <summary>
    /// Starts the talk: the script's init function (InitCommunication), its
    /// description loaded (HailAlien: the first colour map, the song), then
    /// its intro.
    /// </summary>
    public void Start()
    {
        if (Started)
            throw new InvalidOperationException("the talk has started");
        Started = true;
        Check();
        _script.Bind(this);
        _script.Init();
        _screenLoaded = true;
        ColorMap = 0;
        Check();
        _events.Add(new SongChosen(UseAltSong && AltSong != "" ? AltSong : Description.Song));
        _script.Intro();
        Check();
        EndIfSilent();
    }

    /// <summary>The player chooses a reply (SelectResponse): its function runs.</summary>
    public void Choose(int index)
    {
        if (!Started || Ended)
            throw new InvalidOperationException("no replies are offered");
        if (index < 0 || index >= _offered.Count)
            throw new ArgumentOutOfRangeException(nameof(index), $"reply {index} of {_offered.Count}");
        _events.Add(new ReplyChosen(index));
        var (r, func, _) = _offered[index];
        _offered.Clear();
        func(r);
        Check();
        EndIfSilent();
    }

    private void EndIfSilent()
    {
        if (_offered.Count > 0)
        {
            _events.Add(new RepliesOffered(Replies));
            return;
        }
        Ended = true;
        _script.PostEncounter();
        Check();
        _script.Uninit();
        Check();
        _events.Add(new TalkEnded(Segue));
    }

    /// <summary>How the talk ends (getSegue).</summary>
    public Segue Segue => Get("BATTLE_SEGUE") == 0
        ? (_defeated ? Segue.Defeat : Segue.Peace)
        : (_instantVictory ? Segue.Victory : Segue.Hostile);

    /// <summary>setSegue.</summary>
    public void SetSegue(Segue segue)
    {
        switch (segue)
        {
            case Segue.Peace:
                Set("BATTLE_SEGUE", 0);
                break;
            case Segue.Hostile:
                Set("BATTLE_SEGUE", 1);
                break;
            case Segue.Victory:
                _instantVictory = true;
                Set("BATTLE_SEGUE", 1);
                break;
            case Segue.Defeat:
                // UQM also sets the crew to (COUNT)~0 and CHECK_RESTART: the game ends.
                Set("BATTLE_SEGUE", 0);
                _defeated = true;
                break;
        }
    }

    // ------------------------------------------------------------ the phrases

    /// <summary>A phrase's number by its id (the script's enum).</summary>
    public int Phrase(string id) => _script.Phrases.PhraseNumber(id);

    private byte[] Entry(int index) => _phrases[((index - 1) % _phrases.Length + _phrases.Length) % _phrases.Length];

    private int EntryNumber(int index) => ((index - 1) % _phrases.Length + _phrases.Length) % _phrases.Length + 1;

    /// <summary>PHRASE_ENABLED: the phrase's text isn't blank.</summary>
    public bool PhraseEnabled(string id) => Entry(Phrase(id)).Length > 0;

    /// <summary>DISABLE_PHRASE: blanks the phrase's text for the rest of the talk.</summary>
    public void DisablePhrase(string id) => _phrases[EntryNumber(Phrase(id)) - 1] = Array.Empty<byte>();

    /// <summary>PLAYER_SAID.</summary>
    public bool PlayerSaid(int r, string id) => r == Phrase(id);

    /// <summary>NPCPhrase by id.</summary>
    public void NPCPhrase(string id) => NPCPhrase(Phrase(id));

    /// <summary>NPCPhrase (NPCPhrase_cb without a callback): a line, a name, or the alliance's name.</summary>
    public void NPCPhrase(int index)
    {
        switch (index)
        {
            case GlobalPlayerName:
                Splice(null, Name(Ship.CaptainName), 0);
                return;
            case GlobalShipName:
                Splice(null, Name(Ship.ShipName), 0);
                return;
            case 0:
                return;
        }
        if (index < 0)
        {
            // One of the alliance name variants.
            index -= GlobalAllianceName;
            int i = Get("NEW_ALLIANCE_NAME");
            var buf = Entry(index - 1 + i + 1);
            if (i == 3)
                buf = buf.Concat(Name(Ship.CaptainName)).ToArray();
            Splice(null, buf, 0);
            return;
        }
        int n = EntryNumber(index);
        Splice(_clips[n - 1], _phrases[n - 1], n);
    }

    /// <summary>NPCPhrase_splice: a line joined to the page before, with no page break.</summary>
    public void NPCPhraseSplice(string id)
    {
        int n = EntryNumber(Phrase(id));
        if (_clips[n - 1] is null)
            Splice(null, _phrases[n - 1], n);
        else
        {
            Check();
            _events.Add(new MultiTrackQueued(1, _phrases[n - 1]));
        }
    }

    /// <summary>NPCNumber without number speech: the number's text spliced in.</summary>
    public void NPCNumber(int number, string? format = null)
    {
        if (Description.NumberSpeech)
            throw new NotSupportedException("number speech comes with voices");
        Splice(null, Encoding.ASCII.GetBytes(Printf(format ?? "%d", number)), 0);
    }

    private static string Printf(string format, int number) => format switch
    {
        "%d" => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"NPCNumber format {format}"),
    };

    private void Splice(string? clip, byte[] text, int phrase)
    {
        Check();
        _events.Add(new LineQueued(phrase, clip, text));
    }

    private static byte[] Name(string name) => Encoding.UTF8.GetBytes(name);

    // ------------------------------------------------------------ the replies

    /// <summary>shared_phrase_buf, as a reply's text: read when the replies are shown.</summary>
    public static readonly object SharedPhraseBuffer = new();

    /// <summary>
    /// construct_response into shared_phrase_buf: phrase, name, phrase,
    /// name, ... ending after a phrase (the name after it NULL).
    /// </summary>
    public void ConstructResponse(string first, params object[] rest)
    {
        var buf = new List<byte>(Entry(Phrase(first)));
        for (int i = 0; i < rest.Length; i += 2)
        {
            buf.AddRange(Name((string)rest[i]));
            if (i + 1 >= rest.Length)
                break;
            buf.AddRange(Entry(Phrase((string)rest[i + 1])));
        }
        _shared = buf.ToArray();
    }

    /// <summary>Response: offers a reply with its own text.</summary>
    public void Response(string id, Action<int> func) => DoResponsePhrase(Phrase(id), func, shared: false);

    /// <summary>DoResponsePhrase with shared_phrase_buf (<see cref="SharedPhraseBuffer"/>) as its text.</summary>
    public void DoResponsePhrase(string id, Action<int> func, object sharedPhraseBuffer)
    {
        if (!ReferenceEquals(sharedPhraseBuffer, SharedPhraseBuffer))
            throw new ArgumentException("a constructed reply's text is shared_phrase_buf", nameof(sharedPhraseBuffer));
        DoResponsePhrase(Phrase(id), func, shared: true);
    }

    private void DoResponsePhrase(int r, Action<int> func, bool shared)
    {
        Check();
        if (_offered.Count >= MaxReplies)
            throw new InvalidOperationException($"more than {MaxReplies} replies");
        // A reply's text is read when it's shown: a phrase blanked after it's
        // offered shows blank, and shared_phrase_buf as it is then.
        _offered.Add((r, func, shared));
    }

    private TalkReply Reply((int Ref, Action<int> Func, bool Shared) o) =>
        new(o.Ref, o.Func.Method.Name, o.Shared ? _shared : Entry(o.Ref));

    // ------------------------------------------------------------ the story

    /// <summary>GET_GAME_STATE: the shared story, or the talk's own value for a field that isn't shared.</summary>
    public int Get(string field)
    {
        if (StoryState.NotShared.ContainsKey(field))
        {
            _ = _story.Table[field];
            return _own.GetValueOrDefault(field);
        }
        return _story.Get(field);
    }

    /// <summary>SET_GAME_STATE.</summary>
    public void Set(string field, int value)
    {
        Check();
        _events.Add(new StateSet(field, (byte)value));
        if (StoryState.NotShared.ContainsKey(field))
        {
            var f = _story.Table[field];
            if ((byte)value > f.Max)
                throw new InvalidOperationException($"{field} is {(byte)value}, more than its {f.Bits} bits hold");
            _own[field] = (byte)value;
        }
        else
            _story.Set(field, value);
    }

    public StoryState Story => _story;

    /// <summary>TFB_Random, from the talk's own generator.</summary>
    public uint Random()
    {
        Check();
        uint n = _random.Next();
        _events.Add(new RandomDrawn(n));
        return n;
    }

    // ------------------------------------------------------------ the ship

    /// <summary>
    /// DeltaSISGauges' changes to the ship (its drawing is the client's):
    /// crew and fuel within what the ship holds, none going below 0.
    /// </summary>
    public void DeltaSISGauges(int crewDelta, int fuelDelta, int resourceUnitDelta)
    {
        Check();
        _events.Add(new GaugesChanged(crewDelta, fuelDelta, resourceUnitDelta));
        if (crewDelta != 0 && crewDelta != UndefinedDelta)
        {
            if (crewDelta < 0 && Ship.Crew <= -crewDelta)
                Ship.Crew = 0;
            else
                Ship.Crew = Math.Min((ushort)(Ship.Crew + crewDelta), Ship.CrewCapacity);
        }
        if (fuelDelta != 0 && fuelDelta != UndefinedDelta)
        {
            if (fuelDelta < 0 && Ship.Fuel <= (uint)-fuelDelta)
                Ship.Fuel = 0;
            else
                Ship.Fuel = Math.Min((uint)(Ship.Fuel + fuelDelta), Ship.FuelCapacity);
        }
        if (resourceUnitDelta != 0 && resourceUnitDelta != UndefinedDelta)
        {
            if (resourceUnitDelta < 0 && Ship.ResourceUnits <= (uint)-resourceUnitDelta)
                Ship.ResourceUnits = 0;
            else
                Ship.ResourceUnits = (uint)(Ship.ResourceUnits + resourceUnitDelta);
        }
    }

    // ------------------------------------------------------------ changes

    // What the recording program's check_changes does at each engine call
    // and after each script function: the screen and the ship, when they
    // changed since the last look.
    private void Check()
    {
        if (_screenLoaded && (ColorMap != _lastMap || !Ambient.SequenceEqual(_lastAmbient)))
        {
            _lastMap = ColorMap;
            _lastAmbient = Ambient.ToArray();
            _events.Add(new ScreenChanged(ColorMap, _lastAmbient));
        }
        var ship = (Ship.Crew, Ship.Fuel, Ship.Landers, Ship.ResourceUnits, Ship.Minerals.ToArray());
        if (_lastShip is { } last && (last.Item1, last.Item2, last.Item3, last.Item4) == (ship.Item1, ship.Item2, ship.Item3, ship.Item4)
            && last.Item5.SequenceEqual(ship.Item5))
            return;
        if (_lastShip is not null)
            _events.Add(new ShipChanged(ship.Item1, ship.Item2, ship.Item3, ship.Item4, ship.Item5));
        _lastShip = ship;
    }
}
