// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Ported from The Ur-Quan Masters 0.8.0 (src/uqm/commglue.c, comm.c's
// DoResponsePhrase, SelectResponse, HailAlien, AlienTalkSegue and
// DoCommunication, sis.c's DeltaSISGauges, build.c's SetRaceAllied,
// StartSphereTracking, CountEscortShips, HaveEscortShip and
// RemoveSomeEscortShips, libs/sound/trackplayer.c's SpliceTrack,
// SpliceMultiTrack and SplitSubPages as far as NPCPhrase_cb's callbacks
// need them),
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
/// has greeted them). It changes the answerer's ship (<see cref="Ship"/>,
/// decision 4, and the owner's rule of 2026-11-12). Its random numbers come from a generator of its own,
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

    /// <summary>The crew a lost game shows (setSegue (Segue_defeat)'s (COUNT)~0).</summary>
    public const int DefeatedCrew = 0xFFFF;

    private readonly CommScript _script;
    private readonly byte[][] _phrases;
    private readonly int _colorMapCount;
    private readonly StoryState _story;
    private readonly Dictionary<string, int> _own;
    private readonly TfbRandom _random;
    private readonly List<TalkEvent> _events = new();
    private readonly List<(int Ref, Action<int> Func, object? Buffer)> _offered = new();
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
    /// <param name="ship">The answerer's ship.</param>
    /// <param name="clock">The game's clock, which the scripts read the date from (GameClock).</param>
    /// <param name="seed">The scripts' random seed for this talk.</param>
    /// <param name="musicLoads">Whether a music resource loads (the content has it): the alternate song is played only if it does.</param>
    public Talk(CommScript script, CommDescription description, Conversation phrases, int colorMapCount,
        StoryState story, Dictionary<string, int> ownFields, ITalkShip ship, World.GameClock clock, uint seed,
        Func<string, bool> musicLoads)
    {
        Clock = clock;
        _musicLoads = musicLoads;
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
    private readonly Func<string, bool> _musicLoads;

    private static byte[] UntilNul(ReadOnlySpan<byte> raw)
    {
        int n = raw.IndexOf((byte)0);
        return (n < 0 ? raw : raw[..n]).ToArray();
    }

    public CommDescription Description { get; }

    /// <summary>The script the talk runs.</summary>
    public CommScript Script => _script;

    /// <summary>The game's clock (GLOBAL (GameClock)).</summary>
    public World.GameClock Clock { get; }

    /// <summary>usingSpeech: whether the voices play. Not yet: lines are as UQM gives them without the voice add-on.</summary>
    public bool UsingSpeech => false;

    public ITalkShip Ship { get; }

    /// <summary>Everything the talk has done so far.</summary>
    public IReadOnlyList<TalkEvent> Events => _events;

    /// <summary>
    /// Where the numbers of its sets of replies come from (the game's one
    /// counter, stage 3b-1 item 10, so no two sets in the game, in any
    /// talk, share one), set before it starts; without one they count on
    /// from the talk's first round as <see cref="RoundOf"/> says.
    /// </summary>
    internal Func<int>? RoundSource { get; set; }

    private readonly List<int> _offerRounds = new();

    /// <summary>
    /// The number of its <paramref name="offer"/>th set of replies (from 0,
    /// in the order offered): the one <see cref="RoundSource"/> gave it, or
    /// without one <paramref name="firstRound"/> + the offer + 1.
    /// </summary>
    public int RoundOf(int offer, int firstRound) =>
        offer < _offerRounds.Count ? _offerRounds[offer] : firstRound + offer + 1;

    /// <summary>How many sets of replies it has offered.</summary>
    public int Offers => _offerRounds.Count > 0 ? _offerRounds.Count : _events.Count(e => e is RepliesOffered);

    private readonly List<(int At, MineralSale Sale)> _sales = new();

    /// <summary>
    /// The minerals the script sold (SellMinerals), each with how many
    /// events came before it: not an event of UQM's engine (the recording
    /// has none), but what the seller's client counts down.
    /// </summary>
    public IReadOnlyList<(int At, MineralSale Sale)> Sales => _sales;

    /// <summary>The script sells the hold (<paramref name="sale"/>, as it was before): the seller's client draws the counting (SellMinerals).</summary>
    public void SoldMinerals(MineralSale sale) => _sales.Add((_events.Count, sale));

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

    /// <summary>EnableTalkingAnim: the alien's mouth moves with its lines, or not (the Vux beast's report).</summary>
    public void EnableTalkingAnim(bool enable)
    {
        Check();
        _events.Add(new TalkingAnimation(enable));
    }

    /// <summary>
    /// AlienTalkSegue: the lines play up to this track before the script
    /// goes on, the callbacks of their chunks running as they play
    /// (<see cref="PlayTo"/>).
    /// </summary>
    public void AlienTalkSegue(int waitTrack)
    {
        Check();
        _events.Add(new TalkWait(waitTrack));
        PlayTo(waitTrack);
    }

    // ------------------------------------------------------------ the track player

    // What trackplayer.c keeps that NPCPhrase_cb's callbacks need, as the
    // recording program emulates it (tools/extract/golden/comm.c): the
    // tracks queued since the last StopTrack, its static no_page_break
    // (never reset by StopTrack: kept for the talk), the chunks tagged with
    // a callback (by track, in order), how many have run, and comm.c's
    // TalkingFinished.
    private int _trackCount, _tagsRun;
    private bool _noPageBreak, _talkingFinished;
    private readonly List<(int Track, Action Callback)> _tags = new();

    // SplitSubPages' count of a text's pages: each run up to a line end,
    // the line ends after it skipped.
    private static int CountPages(byte[] text)
    {
        int pages = 0, i = 0;
        while (i < text.Length && text[i] != 0)
        {
            while (i < text.Length && text[i] is not (0 or (byte)'\r' or (byte)'\n'))
                ++i;
            while (i < text.Length && text[i] is (byte)'\r' or (byte)'\n')
                ++i;
            ++pages;
        }
        return pages;
    }

    // SpliceTrack's chunks (trackplayer.c): without voices (no timestamp
    // files) a page a chunk, each tagged with the line's callback. A line
    // with a clip starts a track, unless SpliceMultiTrack's no_page_break
    // joins it to the last (its first page then untagged); one without
    // joins the last track's last page, its pages after the first chunks
    // of their own.
    private void QueueChunks(bool newTrack, byte[] text, Action? callback)
    {
        int pages = CountPages(text);
        if (pages == 0)
            return;
        int tagged;
        if (!newTrack)
        {
            if (_trackCount == 0)
                return;   // nothing to append to: UQM warns and drops it
            tagged = pages - 1;
        }
        else
        {
            bool firstUntagged = _noPageBreak;
            if (!(_noPageBreak && _trackCount > 0))
                ++_trackCount;
            tagged = pages - (firstUntagged ? 1 : 0);
            _noPageBreak = false;
        }
        if (callback is not null)
            for (int i = 0; i < tagged; ++i)
                _tags.Add((_trackCount, callback));
    }

    // SpliceMultiTrack after a SpliceTrack (chunks queued): its clips join
    // the last track, and so will the next track (no_page_break).
    private void MultiTrackQueued(int clips)
    {
        if (_trackCount > 0 && clips > 0)
            _noPageBreak = true;
    }

    /// <summary>
    /// Playback reaches the end of track <paramref name="waitTrack"/>
    /// (AlienTalkSegue's TalkSegue, or DoCommunication's play-out with
    /// <see cref="WaitAll"/> once a script function returns): the callbacks
    /// of the chunks up to it run, in order (UQM runs each as its chunk
    /// starts playing, DoTrackTag, on the audio thread; deterministically
    /// here where playback reaches its track: the next track's first
    /// chunk, which UQM starts while the script goes on, waits for the
    /// next segue or the play-out). If no track is left after it, the
    /// talking is finished (TalkingFinished): the segues after it until
    /// the next reply play nothing, and lines queued meanwhile never play.
    /// </summary>
    private void PlayTo(int waitTrack)
    {
        if (_talkingFinished)
            return;
        while (_tagsRun < _tags.Count && _tags[_tagsRun].Track <= waitTrack)
        {
            var callback = _tags[_tagsRun++].Callback;
            Check();
            _events.Add(new CallbackRun(callback.Method.Name));
            callback();
        }
        if (waitTrack >= _trackCount)
            _talkingFinished = true;
    }

    // SelectResponse: StopTrack (what wasn't played is dropped), and the
    // talking not finished.
    private void StopTrack()
    {
        _trackCount = 0;
        _tags.Clear();
        _tagsRun = 0;
        _talkingFinished = false;
    }

    // ------------------------------------------------------------ where the talk is

    /// <summary>
    /// Where the talk is, as the scripts read it (LOBYTE (GLOBAL
    /// (CurrentActivity))): in hyperspace or a system for a race met in
    /// space. Set before the talk loads; SUPER_MELEE (UQM's main menu's)
    /// where nobody sets it, as for the starbase's talks, which don't read it.
    /// </summary>
    public TalkActivity Activity
    {
        get => _activity;
        set => _activity = !Started ? value : throw new InvalidOperationException("the talk has started");
    }

    /// <summary>
    /// The system's own group met (GLOBAL (BattleGroupRef)), named by the
    /// story field of its offset (URQUAN_PROBE_GRPOFFS0 for Sol's drone,
    /// COLONY_GRPOFFS0 for the colony's guard, SAMATRA_GRPOFFS0 ...), or
    /// null for none (0: in hyperspace, or a system of random groups). Set
    /// before the talk loads.
    /// </summary>
    public string? BattleGroup
    {
        get => _battleGroup;
        set => _battleGroup = !Started ? value : throw new InvalidOperationException("the talk has started");
    }

    private TalkActivity _activity;
    private string? _battleGroup;

    /// <summary>GLOBAL (BattleGroupRef) &amp;&amp; GLOBAL (BattleGroupRef) == GET_GAME_STATE_32 (field): the system's own group met is this one.</summary>
    public bool BattleGroupIs(string offsetField) => _battleGroup is not null && _battleGroup == offsetField;

    /// <summary>SetCommIntroMode: how the talk's screen comes in (the hypnotised captain's waking fades it in).</summary>
    public void SetCommIntroMode(CommIntroMode mode, int time)
    {
        Check();
        _events.Add(new IntroModeSet(mode, time));
    }

    // ------------------------------------------------------------ the flow

    /// <summary>
    /// Starts the talk: the script's init function (InitCommunication), its
    /// description loaded (HailAlien: the first colour map, the song), then
    /// its intro.
    /// </summary>
    public void Start()
    {
        Load();
        Hail(fromEncounterScreen: false);
    }

    /// <summary>
    /// InitCommunication's init_race: the script's init function, which sets
    /// how the talk would end now (<see cref="Segue"/>): a talk whose init
    /// leaves it hostile (<see cref="OffersAttack"/>) is met with the
    /// encounter screen first (InitEncounter), the player choosing to hail
    /// (<see cref="Hail"/>) or attack (<see cref="Attack"/>).
    /// </summary>
    public void Load()
    {
        if (Started)
            throw new InvalidOperationException("the talk has started");
        Started = true;
        Check();
        _script.Bind(this);
        _script.Init();
        Check();
    }

    /// <summary>Whether the script's init left the talk hostile (BATTLE_SEGUE): the encounter screen offers Hail and Attack.</summary>
    public bool OffersAttack => Started && !_hailed && !Ended && Get("BATTLE_SEGUE") != 0;

    private bool _hailed;

    /// <summary>
    /// The player hails (HailAlien), after <see cref="Load"/>: from the
    /// encounter screen the talk is set peaceful first (InitCommunication's
    /// SET_GAME_STATE (BATTLE_SEGUE, 0)); then the description is loaded
    /// (the first colour map, the song) and the intro runs.
    /// </summary>
    public void Hail() => Hail(fromEncounterScreen: OffersAttack);

    private void Hail(bool fromEncounterScreen)
    {
        if (!Started || _hailed || Ended)
            throw new InvalidOperationException("the talk isn't loaded, or has been hailed or attacked");
        _hailed = true;
        if (fromEncounterScreen)
            Set("BATTLE_SEGUE", 0);
        _screenLoaded = true;
        ColorMap = 0;
        Check();
        _events.Add(new SongChosen(UseAltSong && AltSong != "" && _musicLoads(AltSong) ? AltSong : Description.Song));
        _script.Intro();
        PlayTo(WaitAll);   // DoCommunication: the lines play out before the replies (or the end)
        Check();
        EndIfSilent();
    }

    /// <summary>
    /// The player attacks from the encounter screen (InitCommunication's
    /// ATTACK): the talk is set hostile, its post-encounter and clean-up
    /// functions run, and it ends without a word (<see cref="Segue"/> hostile).
    /// </summary>
    public void Attack()
    {
        if (!Started || _hailed || Ended)
            throw new InvalidOperationException("the talk isn't loaded, or has been hailed or attacked");
        _hailed = true;
        Set("BATTLE_SEGUE", 1);
        Ended = true;
        _script.PostEncounter();
        Check();
        _script.Uninit();
        Check();
        _events.Add(new TalkEnded(Segue));
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
        StopTrack();
        func(r);
        PlayTo(WaitAll);
        Check();
        EndIfSilent();
    }

    private void EndIfSilent()
    {
        if (_offered.Count > 0)
        {
            // Its number first, so the talk's count of sets (Offers) doesn't
            // mix the events' with the numbers' while the counter runs.
            int? round = RoundSource?.Invoke();
            _events.Add(new RepliesOffered(Replies));
            if (round is int n)
                _offerRounds.Add(n);
            return;
        }
        Ended = true;
        _script.PostEncounter();
        Check();
        _script.Uninit();
        Check();
        _events.Add(new TalkEnded(Segue));
    }

    /// <summary>
    /// The talk has lost the game (setSegue (Segue_defeat)): from then on
    /// it shows the ship's crew as UQM's (COUNT)~0, 65535, which marks the
    /// game lost.
    /// </summary>
    public bool Defeated => _defeated;

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
                // UQM also sets the crew to (COUNT)~0 and CHECK_RESTART, so the
                // game ends (the Ur-Quan's surrender): the talk shows the crew
                // so (<see cref="Defeated"/>), but the ship's own crew is left
                // alone, as the world ends the game, or the ship, its own way
                // once the talk is over (GameWorld.Meetings.cs).
                Set("BATTLE_SEGUE", 0);
                _defeated = true;
                break;
        }
    }

    // ------------------------------------------------------------ the phrases

    /// <summary>A phrase's number by its id (the script's enum).</summary>
    public int Phrase(string id) => _script.Phrases.PhraseNumber(id);

    // SetAbsStringTableIndex (CommData.ConversationPhrases, index - 1): the
    // index is a COUNT (16 bits), and wraps at the table's size.
    private byte[] Entry(int index) => _phrases[EntryNumber(index) - 1];

    private int EntryNumber(int index) => (ushort)(index - 1) % _phrases.Length + 1;

    /// <summary>PHRASE_ENABLED: the phrase's text isn't blank.</summary>
    public bool PhraseEnabled(string id) => Entry(Phrase(id)).Length > 0;

    /// <summary>DISABLE_PHRASE: blanks the phrase's text for the rest of the talk.</summary>
    public void DisablePhrase(string id) => _phrases[EntryNumber(Phrase(id)) - 1] = Array.Empty<byte>();

    /// <summary>PLAYER_SAID.</summary>
    public bool PlayerSaid(int r, string id) => r == Phrase(id);

    /// <summary>NPCPhrase by id.</summary>
    public void NPCPhrase(string id) => NPCPhraseCb(Phrase(id), null);

    /// <summary>
    /// NPCPhrase_cb by id: the line with a callback, a script's method
    /// (named by the line's event and each run's), which runs as the line
    /// plays (each page of it), not as it's queued: the Zoq-Fot-Pik's
    /// SelectAlienZOQ and SelectAlienPIK switching the speaker.
    /// </summary>
    public void NPCPhrase(string id, Action callback) => NPCPhraseCb(Phrase(id), callback);

    /// <summary>NPCPhrase (NPCPhrase_cb without a callback): a line, a name, or the alliance's name.</summary>
    public void NPCPhrase(int index) => NPCPhraseCb(index, null);

    /// <summary>NPCPhrase_cb: a line, a name, or the alliance's name, with a callback run as it plays (or none).</summary>
    public void NPCPhraseCb(int index, Action? callback)
    {
        switch (index)
        {
            case GlobalPlayerName:
                Splice(null, Name(Ship.CaptainName), 0, callback);
                return;
            case GlobalShipName:
                Splice(null, Name(Ship.ShipName), 0, callback);
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
            Splice(null, buf, 0, callback);
            return;
        }
        int n = EntryNumber(index);
        Splice(_clips[n - 1], _phrases[n - 1], n, callback);
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
            _events.Add(new MultiTrackQueued(new[] { _clips[n - 1]! }, _phrases[n - 1]));
            MultiTrackQueued(1);
        }
    }

    /// <summary>
    /// NPCNumber: without number speech the number's text spliced in; with
    /// it (the race's AlienNumberSpeech, <see cref="CommScript.NumberSpeech"/>:
    /// the Slylandro probe's since stage 3b-1 item 8) NPCNumberPhrase, which
    /// UQM runs whenever the race has number speech, voices or not: the
    /// number's words' voice clips and its text spliced onto the page
    /// (SpliceMultiTrack).
    /// </summary>
    public void NPCNumber(int number, string? format = null)
    {
        format ??= "%d";
        if (Description.NumberSpeech)
        {
            var speech = _script.NumberSpeech
                ?? throw new InvalidOperationException($"the {_script.Phrases.Name} script's race speaks numbers but has no number speech");
            var tracks = new List<string?>();
            NumberTracks(speech, number, tracks, toplevel: true);
            Check();
            // SpliceMultiTrack's TrackNames end at the first missing clip.
            int clips = tracks.IndexOf(null);
            var names = tracks.Take(clips < 0 ? tracks.Count : clips).Select(t => t!).ToList();
            _events.Add(new MultiTrackQueued(names, Encoding.ASCII.GetBytes(Printf(format, number))));
            MultiTrackQueued(names.Count);
            return;
        }
        Splice(null, Encoding.ASCII.GetBytes(Printf(format, number)), 0);
    }

    // NPCNumberPhrase: the clips of a number's words, by the speech's digits,
    // largest first (a digit's word, or the number above it spoken in turn;
    // then its name, or the digit's common word: "hundred"); with nothing
    // queued at the top, "zero" (the last digit's word for 0). Returns how
    // many were queued.
    private int NumberTracks(NumberSpeech speech, int number, List<string?> tracks, bool toplevel)
    {
        int queued = 0;
        SpeechDigit? dig = null;
        foreach (var d in speech.Digits)
        {
            dig = d;
            int quot = number / d.Divider;
            if (quot == 0)
                continue;
            quot -= d.Subtrahend;
            if (quot < 0)
                continue;
            if (d.Words is { } words)
            {
                if (quot >= 10)
                    throw new InvalidOperationException($"a digit of {number} past 9");
                if (words[quot] is not { } word)
                    continue;
                tracks.Add(Clip(word));
                queued++;
            }
            else
                queued += NumberTracks(speech, quot, tracks, toplevel: false);
            if (d.Names is { } names)
            {
                foreach (var (divider, maxRemainder, name) in names)
                    if (number % divider <= maxRemainder)
                    {
                        tracks.Add(Clip(name));
                        queued++;
                        break;
                    }
            }
            else if (d.CommonName is { } common)
            {
                tracks.Add(Clip(common));
                queued++;
            }
            number %= d.Divider;
        }
        if (toplevel && queued == 0)
            tracks.Add(Clip(dig!.Words![number]!));
        return queued;
    }

    private string? Clip(string id) => _clips[EntryNumber(Phrase(id)) - 1];

    private static string Printf(string format, int number) => format switch
    {
        "%d" => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        "%03d" => number.ToString("000;-00", System.Globalization.CultureInfo.InvariantCulture),
        _ => throw new NotSupportedException($"NPCNumber format {format}"),
    };

    private void Splice(string? clip, byte[] text, int phrase, Action? callback = null)
    {
        Check();
        _events.Add(new LineQueued(phrase, clip, text, callback?.Method.Name));
        QueueChunks(clip is not null, text, callback);
        if (clip is not null)
            _lineTracks[_events.Count - 1] = _trackCount;
    }

    // Each line with a clip (by its event's index): the track it went into.
    private readonly Dictionary<int, int> _lineTracks = new();

    /// <summary>
    /// The track (from 1 since the last reply, as <see cref="TalkWait"/>
    /// numbers them) that the line with a clip queued at event
    /// <paramref name="eventIndex"/> plays in: a line joined to the last
    /// track (after SpliceMultiTrack) or with no text plays in the last
    /// one; 0 for none before it.
    /// </summary>
    public int TrackOf(int eventIndex) => _lineTracks.GetValueOrDefault(eventIndex);

    private static byte[] Name(string name) => Encoding.UTF8.GetBytes(name);

    // ------------------------------------------------------------ the replies

    /// <summary>shared_phrase_buf, as a reply's text: read when the replies are shown.</summary>
    public static readonly object SharedPhraseBuffer = new();

    /// <summary>
    /// construct_response into shared_phrase_buf: phrase, name, phrase,
    /// name, ... ending after a phrase (the name after it NULL).
    /// </summary>
    public void ConstructResponse(string first, params object[] rest) => _shared = Construct(first, rest);

    /// <summary>construct_response into a buffer of the script's own (starbas.c's buf0).</summary>
    public void ConstructResponse(PhraseBuffer buffer, string first, params object[] rest) =>
        buffer.Text = Construct(first, rest);

    private byte[] Construct(string first, object[] rest)
    {
        var buf = new List<byte>(Entry(Phrase(first)));
        for (int i = 0; i < rest.Length; i += 2)
        {
            buf.AddRange(rest[i] is byte[] bytes ? bytes : Name((string)rest[i]));
            if (i + 1 >= rest.Length)
                break;
            buf.AddRange(Entry(Phrase((string)rest[i + 1])));
        }
        return buf.ToArray();
    }

    /// <summary>
    /// GetAllianceName (commglue.c): the alliance's name as the replies say
    /// it, the phrase <paramref name="name1"/> + NEW_ALLIANCE_NAME, and for
    /// the fourth (3) the captain's name and the phrase after it around it.
    /// </summary>
    public byte[] GetAllianceName(string name1)
    {
        int i = Get("NEW_ALLIANCE_NAME");
        int n = Phrase(name1) + i;
        var buf = new List<byte>(Entry(n));
        if (i == 3)
        {
            buf.AddRange(Name(Ship.CaptainName));
            buf.AddRange(Entry(n + 1));
        }
        return buf.ToArray();
    }

    /// <summary>Response: offers a reply with its own text.</summary>
    public void Response(string id, Action<int> func) => DoResponsePhrase(Phrase(id), func, null);

    /// <summary>
    /// DoResponsePhrase with a constructed text: shared_phrase_buf
    /// (<see cref="SharedPhraseBuffer"/>) or a <see cref="PhraseBuffer"/> of the script's own.
    /// </summary>
    public void DoResponsePhrase(string id, Action<int> func, object buffer)
    {
        if (!ReferenceEquals(buffer, SharedPhraseBuffer) && buffer is not PhraseBuffer)
            throw new ArgumentException("a constructed reply's text is shared_phrase_buf or a PhraseBuffer", nameof(buffer));
        DoResponsePhrase(Phrase(id), func, buffer);
    }

    private void DoResponsePhrase(int r, Action<int> func, object? buffer)
    {
        Check();
        if (_offered.Count >= MaxReplies)
            throw new InvalidOperationException($"more than {MaxReplies} replies");
        // A reply's text is read when it's shown: a phrase blanked after it's
        // offered shows blank, and a constructed buffer as it is then.
        _offered.Add((r, func, buffer));
    }

    private TalkReply Reply((int Ref, Action<int> Func, object? Buffer) o) =>
        new(o.Ref, o.Func.Method.Name, o.Buffer switch
        {
            null => Entry(o.Ref),
            PhraseBuffer b => b.Text,
            _ => _shared,
        });

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

    /// <summary>GET_GAME_STATE_32: the four 8-bit fields from <paramref name="first"/>, low byte first.</summary>
    public uint Get32(string first)
    {
        var f = _story.Table[first];
        uint v = 0;
        for (int i = 0; i < 4; ++i)
            v |= (uint)Get(FieldAt(f.StartBit + 8 * i)) << (8 * i);
        return v;
    }

    /// <summary>SET_GAME_STATE_32: each of the four bytes set in turn, low byte first.</summary>
    public void Set32(string first, uint value)
    {
        var f = _story.Table[first];
        for (int i = 0; i < 4; ++i, value >>= 8)
            Set(FieldAt(f.StartBit + 8 * i), (byte)value);
    }

    private string FieldAt(int startBit) =>
        _story.Table.Fields.FirstOrDefault(x => x.StartBit == startBit && x.Bits == 8)?.Name
        ?? throw new InvalidOperationException($"no 8-bit field at bit {startBit}");

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

    /// <summary>
    /// AddEscortShips: ships of a race (races.h's number) join the escort
    /// fleet (the host's, whoever answers: stage 4b, decision 3), as many
    /// as it has room for, their captains named from the talk's generator
    /// (NameCaptain's TFB_Random); then DeltaSISGauges redraws everything.
    /// Returns how many joined.
    /// </summary>
    public int AddEscortShips(int race, int count)
    {
        Check();
        int added = Ship.AddEscorts(race, count, Random);
        _events.Add(new EscortsAdded(race, count, added));
        Check();
        DeltaSISGauges(UndefinedDelta, UndefinedDelta, UndefinedDelta);
        return added;
    }

    /// <summary>
    /// SetEscortCrewComplement: the first escort of a race (races.h's
    /// number) with its race's starting crew gets <paramref name="crew"/>
    /// crew and the captain <paramref name="captain"/> (a name index), if
    /// there's one (the Spathi's Fwiffo, alone in the Eluder that joins).
    /// </summary>
    public void SetEscortCrewComplement(int race, int crew, int captain)
    {
        Check();
        _events.Add(new EscortCrewSet(race, crew, captain));
        Ship.SetEscortCrewComplement(race, crew, captain);
    }

    /// <summary>
    /// AddEvent: a clock event, relative to today or on a date, by gameev.h's
    /// name, queued, run or set aside by the story (StoryState.AddEvent; the
    /// Ilwrath's ADVANCE_ILWRATH_MISSION is set aside until stage 3c).
    /// </summary>
    public void AddEvent(bool relative, int months, int days, int years, string evt)
    {
        Check();
        _events.Add(new EventAdded(relative, months, days, years, evt));
        _story.AddEvent(relative, months, days, years, evt, Clock);
    }

    /// <summary>CheckAlliance: a race's standing, by races.h's number.</summary>
    public Story.Allied CheckAlliance(int race) => _story.Alliances[race];

    // Whether races.h's number names a race in avail_race_q (GetStarShipFromIndex).
    private bool IsRace(int race) => race >= 0 && race < _story.Rules.Races.Count;

    /// <summary>
    /// SetRaceAllied (build.c:210): a race's alliance made
    /// (<paramref name="flag"/>) or ended, which changes the shipyard
    /// (<see cref="StoryState.SetRaceAllied"/>: a dead race stays dead).
    /// Returns 1, or 0 for no such race.
    /// </summary>
    public int SetRaceAllied(int race, bool flag)
    {
        Check();
        int result = 0;
        bool changed = false;
        if (IsRace(race))
        {
            changed = _story.SetRaceAllied(_story.Rules.Races[race], flag);
            result = 1;
        }
        _events.Add(new RaceAllied(race, flag, result, changed));
        return result;
    }

    /// <summary>
    /// StartSphereTracking (build.c:240), from a script: the race's sphere
    /// shown on the starmap from now on (<see cref="StoryState.StartSphereTracking"/>).
    /// Returns the race's number, or 0 if it's extinct or no such race (as
    /// UQM, 0 for the Arilou too, whose number it is, though their sphere is
    /// tracked).
    /// </summary>
    public int StartSphereTracking(int race)
    {
        Check();
        int result = IsRace(race) && _story.StartSphereTracking(_story.Rules.Races[race]) ? race : 0;
        _events.Add(new SphereTracked(race, result));
        return result;
    }

    /// <summary>
    /// CountEscortShips (build.c:276): how many ships of a race the escort
    /// fleet has (the host's, whoever answers: the owner's question 4 of the
    /// stage 3b-2 plan); 0 for no such race.
    /// </summary>
    public int CountEscortShips(int race) => IsRace(race) ? Ship.EscortRaces.Count(r => r == race) : 0;

    /// <summary>HaveEscortShip (build.c:308): whether the escort fleet has a ship of a race.</summary>
    public bool HaveEscortShip(int race) => CountEscortShips(race) > 0;

    /// <summary>
    /// GetHeadLink (&amp;GLOBAL (built_ship_q)) == 0: the escort fleet has
    /// no ships (the Druuge's check for a ship to salvage, druugec.c:822).
    /// </summary>
    public bool EscortFleetEmpty => Ship.EscortRaces.Count == 0;

    /// <summary>
    /// RemoveSomeEscortShips (build.c:358): up to <paramref name="count"/>
    /// ships of a race (races.h's number) leave the escort fleet, the first
    /// first. Returns, as UQM's does, the count it couldn't remove (not the
    /// number removed its comment says). Its DeltaSISGauges redraw runs
    /// whenever some of the count is left: for <see cref="RemoveEscortShips"/>
    /// nearly always.
    /// </summary>
    public int RemoveSomeEscortShips(int race, int count)
    {
        if (count == 0)
            return 0;
        Check();
        int removed = Ship.RemoveEscorts(race, count);
        _events.Add(new EscortsRemoved(race, count, removed));
        count -= removed;
        if (count > 0)
            DeltaSISGauges(UndefinedDelta, UndefinedDelta, UndefinedDelta);
        return count;
    }

    /// <summary>RemoveEscortShips (build.c:400): every ship of a race leaves the escort fleet (RemoveSomeEscortShips with (COUNT) -1).</summary>
    public void RemoveEscortShips(int race) => RemoveSomeEscortShips(race, 0xFFFF);

    // ------------------------------------------------------------ changes

    // What the recording program's check_changes does at each engine call
    // and after each script function: the screen, the ship and its bio
    // data, when they changed since the last look.
    private void Check()
    {
        if (_screenLoaded && (ColorMap != _lastMap || !Ambient.SequenceEqual(_lastAmbient)))
        {
            _lastMap = ColorMap;
            _lastAmbient = Ambient.ToArray();
            _events.Add(new ScreenChanged(ColorMap, _lastAmbient));
        }
        var ship = (_defeated ? DefeatedCrew : Ship.Crew, Ship.Fuel, Ship.Landers, Ship.ResourceUnits, Ship.Minerals.ToArray());
        if (_lastShip is not { } last || (last.Item1, last.Item2, last.Item3, last.Item4) != (ship.Item1, ship.Item2, ship.Item3, ship.Item4)
            || !last.Item5.SequenceEqual(ship.Item5))
        {
            if (_lastShip is not null)
                _events.Add(new ShipChanged(ship.Item1, ship.Item2, ship.Item3, ship.Item4, ship.Item5));
            _lastShip = ship;
        }
        // The bio data, an event of its own (the recording's "bio").
        int bio = Ship.BioData;
        if (_lastBio is int b && b != bio)
            _events.Add(new BioChanged(bio));
        _lastBio = bio;
        // The answering ship's own slots (the Melnorme's strip) and the
        // modules' prices (the Melnorme's technology), each when it changes.
        var slots = new SlotsChanged(Ship.OwnDriveSlots.ToArray(), Ship.OwnJetSlots.ToArray(), Ship.OwnModuleSlots.ToArray());
        if (_lastSlots is { } ls && !ls.Equals(slots))
            _events.Add(slots);
        _lastSlots = slots;
        // The prices only as this talk changed them (SetModuleCost): another
        // player's purchase meanwhile isn't this talk's (UQM's one talk is
        // the only one to change them), though the prices printed are all of
        // them as they stand.
        if (_costsWere is { } was)
        {
            var costs = new CostsChanged(PurchaseModuleCosts()) { Was = was };
            if (!costs.Costs.SequenceEqual(was))
                _events.Add(costs);
            _costsWere = null;
        }
    }

    private int? _lastBio;
    private SlotsChanged? _lastSlots;
    private int[]? _costsWere;

    /// <summary>NUM_PURCHASE_MODULES: the modules with a price, PLANET_LANDER to ANTIMISSILE_DEFENSE.</summary>
    public const int PurchaseModules = 14;

    // GLOBAL (ModuleCost[0 .. NUM_PURCHASE_MODULES - 1]), by sis.h's numbers.
    private int[] PurchaseModuleCosts()
    {
        var costs = new int[PurchaseModules];
        for (int i = 0; i < PurchaseModules; ++i)
            costs[i] = ModuleCost(i);
        return costs;
    }

    /// <summary>GLOBAL (ModuleCost[module]), by sis.h's number (0 for one the rules don't price).</summary>
    public int ModuleCost(int module)
    {
        string name = World.ShipModules.Names[module];
        return _story.Rules.Modules.Contains(name) ? _story.ModuleCost(name) : 0;
    }

    /// <summary>GLOBAL (ModuleCost[module]) = cost: the shared price (a module goes on sale for everyone).</summary>
    public void SetModuleCost(int module, int cost)
    {
        // Told at the next look (Check), as the recorder prints the prices it sees changed.
        _costsWere ??= PurchaseModuleCosts();
        _story.SetModuleCost(World.ShipModules.Names[module], cost);
    }

    // ------------------------------------------------------------ the status area

    /// <summary>
    /// Whether the status area's message shows the answerer's Melnorme
    /// credits (SetStatusMessageMode (SMM_CREDITS)), as the Melnorme's talk
    /// sets it for its time, closing lines too; drawn by the client, not recorded.
    /// </summary>
    public bool ShowsCredits { get; set; }

    private readonly List<(int At, BioSale Sale)> _bioSales = new();

    /// <summary>
    /// The bio data the script sold (the Melnorme's DoSell), each with how
    /// many events came before it: sold at once on the host, as the
    /// recording sells it (a key pressed at once), the count-down on the
    /// cargo display the seller's client's to draw.
    /// </summary>
    public IReadOnlyList<(int At, BioSale Sale)> BioSales => _bioSales;

    /// <summary>The script sells the bio data aboard (<paramref name="sale"/>, as it was before): the seller's client counts it down.</summary>
    public void SoldBioData(BioSale sale) => _bioSales.Add((_events.Count, sale));
}

/// <summary>A reply's text a script builds with construct_response into a buffer of its own.</summary>
public sealed class PhraseBuffer
{
    public byte[] Text { get; internal set; } = Array.Empty<byte>();
}
