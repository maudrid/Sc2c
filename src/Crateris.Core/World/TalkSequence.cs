// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;

namespace Crateris.Core.World;

/// <summary>
/// A run of talks one player answers at a time, what a visit to Earth's
/// starbase (<see cref="StarbaseVisit"/>) and a story pause
/// (<see cref="StoryPause"/>, stage 3c item 2) share: its talks in order,
/// who replies to them (the starter, or who the replies passed to), how
/// the replier has paged the lines for those watching, the sets of
/// replies' numbers, and who sold the minerals in each sale. A story one
/// is shown to everyone (stage 3, decisions 6 and 7: <see cref="TalkView.Of(TalkSequence?)"/>),
/// and pauses the game while it is.
/// </summary>
public abstract class TalkSequence
{
    private protected readonly GameWorld _world;

    // The talks' ship: the answerer's (AnswererTalkShip), made with the
    // first talk.
    private protected AnswererTalkShip? _ship;

    // Who sold the minerals in each sale, by talk and the sale's event.
    private readonly Dictionary<(int Talk, int At), Player> _sellers = new();
    private int _talks;

    // The talks that were story as they started (TalkRules).
    private readonly HashSet<Talk> _storyTalks = new();

    private protected TalkSequence(GameWorld world, Player starter, int firstRound)
    {
        _world = world;
        Starter = starter;
        Replier = starter;
        FirstRound = firstRound;
    }

    /// <summary>Who started it: the player whose talks they are first.</summary>
    public Player Starter { get; }

    /// <summary>
    /// Who replies: the starter, or, once they've left, the next player in
    /// join order, the host first (decision 7). The talks are the
    /// answerer's ship's (the owner's rule, 2026-11-12: the group as a whole
    /// starts the story): its names, its hold, crew and fuel, the ship
    /// changing with the answerer (<see cref="AnswererTalkShip"/>).
    /// </summary>
    public Player Replier { get; internal set; }

    /// <summary>How the replier has paged the current lines (decision 7): what watchers follow; null before any.</summary>
    public TalkPaging? Paging { get; internal set; }

    /// <summary>Where it is: the step a talk's view carries (<see cref="TalkView.Step"/>).</summary>
    public VisitStep Step { get; private protected set; }

    /// <summary>The talk going on, if any (none while time passes, or once done).</summary>
    public Talk? Talk { get; private protected set; }

    /// <summary>Every talk so far, in order, the one going on last.</summary>
    public List<Talk> Talks { get; } = new();

    /// <summary>
    /// The sets of replies offered in the game's earlier talks: rounds
    /// count on from them, so a round names one set of replies in the
    /// whole game, and a client that has answered one can't mistake the
    /// next talk's for it.
    /// </summary>
    public int FirstRound { get; }

    /// <summary>
    /// Whether it is story now (decision 6, <see cref="TalkRules"/>): its
    /// talk is, or time is passing. A story one pauses the game for
    /// everyone, and everyone is shown it.
    /// </summary>
    public abstract bool Story { get; }

    /// <summary>Whether any of its talks was story, or time passed: everyone was shown it (decision 6).</summary>
    public bool WasStory { get; private protected set; }

    /// <summary>Whether it was taken back (a visit's opening, <see cref="StarbaseVisit.Undo"/>).</summary>
    public bool Undone { get; private protected set; }

    /// <summary>Whether it's over.</summary>
    public bool Done => Step == VisitStep.Done;

    /// <summary>Its time skip, once it has one (the starbase's two weeks; a story pause's days); null for none.</summary>
    public virtual TimeSkip? Skip => null;

    // Whether the talk going on was story as it started.
    private protected bool TalkIsStory;

    /// <summary>
    /// Whether one of its talks was story as it started (decision 6): shown
    /// to everyone, so what it did isn't told again in notices
    /// (<see cref="GameWorld.TellTalkEffects"/>).
    /// </summary>
    internal bool IsStoryTalk(Talk talk) => _storyTalks.Contains(talk);

    /// <summary>
    /// The set of replies offered now (its number in the game: the last its
    /// talks offered, each numbered by the game's one counter, stage 3b-1
    /// item 10), or <see cref="FirstRound"/> before any.
    /// </summary>
    public int Round
    {
        get
        {
            int round = FirstRound, before = 0;
            foreach (var t in Talks)
            {
                int n = t.Offers;
                if (n > 0)
                    round = t.RoundOf(n - 1, FirstRound + before);
                before += n;
            }
            return round;
        }
    }

    /// <summary>The numbers of every set of replies its talks have offered, in order.</summary>
    public IEnumerable<int> Rounds()
    {
        int before = 0;
        foreach (var t in Talks)
        {
            int n = t.Offers;
            for (int i = 0; i < n; ++i)
                yield return t.RoundOf(i, FirstRound + before);
            before += n;
        }
    }

    /// <summary>Who sold the minerals in a talk's sale (its talk's index in <see cref="Talks"/>, the sale's event), or null.</summary>
    public Player? SellerOf(int talk, int at) => _sellers.GetValueOrDefault((talk, at));

    /// <summary>The replier chooses a reply in the talk going on. False if none is offered.</summary>
    internal bool Choose(int index)
    {
        if (Talk is not { Ended: false } talk || index < 0 || index >= talk.Replies.Count)
            return false;
        int sales = talk.Sales.Count;
        var seller = Replier;
        talk.Choose(index);
        foreach (var (at, _) in talk.Sales.Skip(sales))
            _sellers[(Talks.Count - 1, at)] = seller;
        if (talk.Ended)
            TalkEnded();
        return true;
    }

    /// <summary>The talk going on has ended: what comes next.</summary>
    private protected abstract void TalkEnded();

    /// <summary>
    /// A talk starts, as step <paramref name="step"/>: its own random seed
    /// (decision 12, the same for the same game frame, starter and talk),
    /// story or not as <paramref name="story"/> says, the answerer's ship,
    /// how its screen comes in (<paramref name="intro"/>: the
    /// SetCommIntroMode its caller made before InitCommunication, null for
    /// the default), and its first lines. <see cref="BeforeTalk"/> runs
    /// before it's made (a visit tracks its race's sphere there, as
    /// InitCommunication does).
    /// </summary>
    private protected void StartTalk(VisitStep step, string script, Dictionary<string, int> own, bool story,
        CommIntro? intro = null)
    {
        Step = step;
        uint seed = unchecked((uint)_world.Frame * 2_654_435_761u + (uint)++_talks * 40_503u);
        foreach (char c in Starter.Id)
            seed = unchecked(seed * 31 + c);
        TalkIsStory = story;
        WasStory |= story;
        BeforeTalk(script);
        var library = _world.TalkLibrary!;
        _ship ??= new AnswererTalkShip(() => Replier, library.Strings, _world);
        Talk = library.Create(script, _world.Story!, own, _ship, _world.Clock, seed);
        Talk.IntroBefore = intro;
        Talk.RoundSource = _world.NewRound;
        Talks.Add(Talk);
        if (story)
            _storyTalks.Add(Talk);
        Talk.Start();
        if (Talk.Ended)
            TalkEnded();
    }

    /// <summary>What comes before a talk is made (InitCommunication's StartSphereTracking): nothing by default.</summary>
    private protected virtual void BeforeTalk(string script)
    {
    }
}
