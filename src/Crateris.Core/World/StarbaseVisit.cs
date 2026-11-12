// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/starbase.c's VisitStarBase
// and DoTimePassage; src/uqm/comm.c's InitCommunication, which ends a
// hostile talk in EncounterBattle), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Comm;
using Crateris.Core.Story;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

/// <summary>Where a visit to Earth's starbase is (VisitStarBase's order).</summary>
public enum VisitStep
{
    /// <summary>The commander's talk before the alliance (comandr.c).</summary>
    Commander,

    /// <summary>The Ilwrath ship that answers the probe's call (ilwrathc.c), whose talk ends in its battle (<see cref="IlwrathFight"/>).</summary>
    Ilwrath,

    /// <summary>The commander again, impressed: the alliance and its name.</summary>
    Alliance,

    /// <summary>Two weeks pass, the screen fading to black over two seconds (DoTimePassage).</summary>
    TimePassing,

    /// <summary>The allied commander: the starbase is ready (starbas.c).</summary>
    StarbaseReady,

    /// <summary>Docked at the allied starbase, at its menu (DoStarBase): talk, outfit, the shipyard, or leave.</summary>
    Menu,

    /// <summary>Talking to the allied commander from the menu: an ordinary talk, under the dialogue lock.</summary>
    MenuTalk,

    /// <summary>Over: the ship has left the starbase (or never docked: before the alliance).</summary>
    Done,

    /// <summary>
    /// The Ilwrath's battle by Earth, after its talk (stage 4a item 9): no
    /// talk, no story pause; the starter fights it in the world
    /// (<see cref="StarbaseVisit.Fight"/>) and the visit waits for its end.
    /// Part of the opening, between <see cref="Ilwrath"/> and
    /// <see cref="Alliance"/>; numbered last so the steps a talk's view
    /// carries keep their numbers on the wire (it's never sent: a player is
    /// shown the Ilwrath's closing lines meanwhile).
    /// </summary>
    IlwrathFight,
}

/// <summary>
/// One player's visit to Earth's starbase, as VisitStarBase runs it
/// (stage 3a, items 5 and 8). Before the alliance, the commander talks;
/// if he warns of the Ilwrath, their ship talks next, and its talk ends
/// in a battle by Earth that the starter fights (stage 4a item 9, stage 4
/// decision 13: <see cref="VisitStep.IlwrathFight"/>, which the world
/// runs); the Ilwrath killed, the commander talks again and makes the
/// alliance. With the Lunar base aboard, two weeks pass and the
/// allied commander says the starbase is ready, with 2,500 resource
/// units, which are split between the players in the game (decision 5).
/// Then, once allied, the ship is docked at the starbase's menu
/// (<see cref="VisitStep.Menu"/>, DoStarBase) until it leaves: from there
/// its player talks to the commander (an ordinary talk, under the dialogue
/// lock), outfits the ship and hires crew. Touching the starbase later,
/// with nothing for the opening to tell, docks at once.
/// <para>
/// The opening (the steps before the menu) can be taken back: the story
/// fields its talks wrote (only those: another player's lander may take
/// the Lunar base meanwhile), the starter's ship and the two weeks. A save
/// made during it is of the game without it, as UQM can't save in the
/// middle of one; and if the starter leaves with no one to take the
/// replies, or every player leaves the Ilwrath's fight with the Ilwrath
/// alive (<see cref="GameWorld"/>'s AfterTheFight), or the starter is gone
/// as that fight would start, it's undone. Once docked,
/// nothing is taken back: leaving ends the visit, as departing does.
/// </para>
/// </summary>
public sealed class StarbaseVisit
{
    /// <summary>LOST_DAYS: the days the starbase's building takes.</summary>
    public const int LostDays = 14;

    /// <summary>DoTimePassage's fade: two seconds, in world frames.</summary>
    public const int TimePassingFrames = 2 * GameClock.FramesPerSecond;

    /// <summary>The Lunar base's gift (DeltaSISGauges (0, 0, 2500)).</summary>
    public const int StarbaseGift = 2500;

    private readonly GameWorld _world;
    private int _fadeLeft;
    private uint _unitsBefore;
    private int _talks;
    private bool _skipped;

    // STARBASE_VISITED while docked: whether the allied commander has
    // greeted this player on this visit (decision 11), as his talks leave it.
    private int _greeted;

    internal StarbaseVisit(GameWorld world, Player starter, int firstRound = 0)
    {
        FirstRound = firstRound;
        _world = world;
        Starter = starter;
        Replier = starter;
        var story = world.Story!;
        StoryBefore = story.Save();
        HoldBefore = starter.Ship.Hold.Save();
        FuelBefore = starter.Ship.Flight.Fuel;
        MetBefore = world.CommanderMet;
        if (story.Get("STARBASE_AVAILABLE") == 0)
            StartTalk(VisitStep.Commander, "comandr", new() { ["STARBASE_VISITED"] = world.CommanderMet ? 1 : 0 });
        else
            AfterAlliance();
    }

    public Player Starter { get; }

    /// <summary>
    /// Who replies: the starter, or, once they've left, the next player in
    /// join order, the host first (decision 7). The talk is still the
    /// starter's: their ship and names.
    /// </summary>
    public Player Replier { get; internal set; }

    /// <summary>How the replier has paged the current lines (decision 7): what watchers follow; null before any.</summary>
    public Comm.TalkPaging? Paging { get; internal set; }

    /// <summary>Whether the visit is still in the opening (before the menu, the Ilwrath's fight too): one at a time, taken back if its starter leaves.</summary>
    public bool Opening => Step < VisitStep.Menu || Step == VisitStep.IlwrathFight;

    /// <summary>
    /// The Ilwrath's battle by Earth, once its talk has ended in it
    /// (<see cref="VisitStep.IlwrathFight"/>; the world starts it and
    /// settles the visit as it ends); null before.
    /// </summary>
    public WorldBattle? Fight { get; internal set; }

    /// <summary>Docked at the menu, not talking: the ship may be outfitted, crew hired, a talk started, or the starbase left.</summary>
    public bool AtMenu => Step == VisitStep.Menu;

    /// <summary>
    /// Whether the visit is story now (decision 6, <see cref="Comm.TalkRules"/>):
    /// its talk is, or time is passing. A story visit pauses the game for
    /// everyone, and everyone is shown it.
    /// </summary>
    public bool Story => Step == VisitStep.TimePassing || (Talk is not null && _talkIsStory);

    private bool _talkIsStory;

    public VisitStep Step { get; private set; }

    /// <summary>The talk going on, if any (none while time passes, or once done).</summary>
    public Talk? Talk { get; private set; }

    /// <summary>Every talk of the visit so far, in order, the one going on last.</summary>
    public List<Talk> Talks { get; } = new();

    /// <summary>World frames left of the fade while time passes.</summary>
    public int FadeLeft => _fadeLeft;

    /// <summary>The story state when the visit started.</summary>
    public StoryState.Saved StoryBefore { get; }

    /// <summary>The starter's hold when the visit started.</summary>
    public HoldState HoldBefore { get; }

    /// <summary>The starter's fuel when the visit started.</summary>
    public uint FuelBefore { get; }

    /// <summary>Whether the group had met the commander when the visit started.</summary>
    public bool MetBefore { get; }

    public bool Done => Step == VisitStep.Done;

    /// <summary>How many sets of replies the visit's talks have offered: the set offered now is this one.</summary>
    public int Round => FirstRound + Talks.Sum(t => t.Events.Count(e => e is RepliesOffered));

    /// <summary>
    /// The sets of replies offered in the game's earlier visits: rounds
    /// count on from them, so a round names one set of replies in the
    /// whole game, and a client that has answered one can't mistake the
    /// next visit's for it.
    /// </summary>
    public int FirstRound { get; }

    /// <summary>Whether any of its talks was story, or time passed: everyone was shown it (decision 6).</summary>
    public bool WasStory { get; private set; }

    /// <summary>
    /// Whether it was undone: its replier left with no one to take the
    /// replies; every player left the Ilwrath's fight with the Ilwrath alive
    /// (destroyed or disconnected); or its starter was gone as that fight
    /// was to start.
    /// </summary>
    public bool Undone { get; private set; }

    /// <summary>
    /// Whether the starter's part of the visit is settled: it disconnected
    /// from the Ilwrath's fight, and its hold and fuel were put back as
    /// they were before the visit, less the retreat's fuel, there and then
    /// (<see cref="SettleStarter"/>). From then on its ship is its own again:
    /// what it does after (back in the game, outside the fight) is neither
    /// taken back with the visit nor left out of a save.
    /// </summary>
    public bool StarterSettled { get; private set; }

    /// <summary>The starter chooses a reply in the talk going on. False if none is offered.</summary>
    internal bool Choose(int index)
    {
        if (Talk is not { Ended: false } talk || index < 0 || index >= talk.Replies.Count)
            return false;
        talk.Choose(index);
        if (talk.Ended)
            TalkEnded();
        return true;
    }

    /// <summary>A world frame: the fade while time passes.</summary>
    internal void Frame()
    {
        if (Step != VisitStep.TimePassing)
            return;
        if (--_fadeLeft > 0)
            return;
        // MoveGameClockDays: the day starts afresh (tick_count = day_in_ticks);
        // the clock's events wait for stage 3c.
        for (int i = 0; i < LostDays; ++i)
            _world.Clock.NextDay();
        _world.Clock.FramesIntoDay = 0;
        _skipped = true;
        // The allied commander, at the starbase (GLOBAL_FLAGS_AND_DATA ~0);
        // whether he has greeted this player on this visit is theirs, and
        // the visit is new.
        _unitsBefore = Starter.Ship.Hold.ResourceUnits;
        StartTalk(VisitStep.StarbaseReady, "starbas", new() { ["GLOBAL_FLAGS_AND_DATA"] = 0xFF });
    }

    private void StartTalk(VisitStep step, string script, Dictionary<string, int> own)
    {
        Step = step;
        // The talk's own random seed (decision 12), the same for the same
        // game frame, player and talk of the visit.
        uint seed = unchecked((uint)_world.Frame * 2_654_435_761u + (uint)++_talks * 40_503u);
        foreach (char c in Starter.Id)
            seed = unchecked(seed * 31 + c);
        _talkIsStory = Comm.TalkRules.IsStory(script, _world.Story!);
        WasStory |= _talkIsStory;
        Talk = _world.TalkLibrary!.Create(script, _world.Story!, own,
            new PlayerTalkShip(Starter, _world.TalkLibrary.Strings, _world.Host.Ship), _world.Clock, seed);
        Talks.Add(Talk);
        Talk.Start();
        if (Talk.Ended)
            TalkEnded();
    }

    private void TalkEnded()
    {
        var story = _world.Story!;
        var talk = Talk!;
        switch (Step)
        {
            case VisitStep.Commander:
                _world.CommanderMet = talk.OwnFields.GetValueOrDefault("STARBASE_VISITED") != 0;
                if (story.Get("PROBE_ILWRATH_ENCOUNTER") == 0)
                    Finish();
                else
                {
                    // BuildBattle, before a talk with a ship in a system:
                    // ESCAPE_COUNTER, the starter's ship's own (stage 4a item 6).
                    if (Starter.Ship.Ip is { } ip)
                        ip.EscapeCounter = IpFlight.EscapeFrames;
                    StartTalk(VisitStep.Ilwrath, "ilwrath", new());
                }
                break;
            case VisitStep.Ilwrath:
                // InitCommunication: a hostile ending (every reply at Earth
                // is) builds the player's side and fights (EncounterBattle):
                // the world starts the battle (GameWorld.AfterVisit) and
                // the visit waits for it. Without the battle's content (a
                // world of the talks alone, some tests') it counts as won,
                // as stage 3a had it; a peaceful ending goes on at once,
                // as VisitStarBase does after any talk that doesn't kill
                // the flagship.
                if (talk.Segue == Segue.Hostile && _world.BattleKit is not null)
                {
                    Step = VisitStep.IlwrathFight;
                    Talk = null;
                }
                else
                    AfterTheFight(Replier);
                break;
            case VisitStep.Alliance:
                _world.CommanderMet = talk.OwnFields.GetValueOrDefault("STARBASE_VISITED") != 0;
                // UQM's one STARBASE_VISITED goes on into the menu's talks
                // as the commander's alliance left it.
                _greeted = talk.OwnFields.GetValueOrDefault("STARBASE_VISITED");
                AfterAlliance();
                break;
            case VisitStep.StarbaseReady:
                ShareTheGift();
                _greeted = talk.OwnFields.GetValueOrDefault("STARBASE_VISITED");
                Dock();
                break;
            case VisitStep.MenuTalk:
                _greeted = talk.OwnFields.GetValueOrDefault("STARBASE_VISITED");
                Dock();
                break;
        }
    }

    /// <summary>
    /// The Ilwrath killed (VisitStarBase after its battle): the commander's
    /// alliance talk, answered by <paramref name="replier"/> (the starter, or
    /// the next player if the starter can't, decision 13).
    /// </summary>
    internal void AfterTheFight(Player replier)
    {
        Replier = replier;
        StartTalk(VisitStep.Alliance, "comandr", new() { ["STARBASE_VISITED"] = _world.CommanderMet ? 1 : 0 });
    }

    // With the Lunar base aboard (or the Chmmr's bomb just installed, in
    // 3c), time passes and the allied commander talks; else the starbase's
    // menu.
    private void AfterAlliance()
    {
        var story = _world.Story!;
        if (story.Get("MOONBASE_ON_SHIP") != 0 || story.Get("CHMMR_BOMB_STATE") == 2)
        {
            Step = VisitStep.TimePassing;
            WasStory = true;
            Talk = null;
            _fadeLeft = TimePassingFrames;
        }
        else
            Dock();
    }

    // DoStarBase: the menu, with the commander's talk over.
    private void Dock()
    {
        Step = VisitStep.Menu;
        Talk = null;
        Menu ??= new Starbase.StarbaseMenu();
        Menu.Screen = Starbase.StarbaseScreen.Menu;
        Menu.Opened();
    }

    /// <summary>The docked player's screens (the menu, outfitting, the shipyard), once docked.</summary>
    public Starbase.StarbaseMenu? Menu { get; private set; }

    /// <summary>
    /// The menu's Talk (TALK_COMMANDER): the allied commander, at the
    /// starbase (GLOBAL_FLAGS_AND_DATA ~0), greeting this player if he
    /// hasn't this visit. The world takes the dialogue lock first. False if
    /// not at the menu.
    /// </summary>
    internal bool TalkToCommander()
    {
        if (!AtMenu)
            return false;
        StartTalk(VisitStep.MenuTalk, "starbas", new() { ["GLOBAL_FLAGS_AND_DATA"] = 0xFF, ["STARBASE_VISITED"] = _greeted });
        return true;
    }

    /// <summary>
    /// The menu's Leave (DEPART_BASE): the ship is back in flight by the
    /// starbase, and the commander will greet this player again next time
    /// (STARBASE_VISITED 0). False if not at the menu.
    /// </summary>
    internal bool Depart()
    {
        if (!AtMenu)
            return false;
        _greeted = 0;
        Finish();
        return true;
    }

    /// <summary>
    /// The starter leaves the game while docked (or is away as their
    /// opening docks): the visit ends, its talk with it, nothing taken
    /// back; the bulletins the talk gave stay given.
    /// </summary>
    internal void Abandon()
    {
        if (Opening)
            throw new InvalidOperationException("an opening is undone, not abandoned");
        if (Talk is { Ended: false, Script: Comm.Scripts.StarbaseScript script })
            script.KeepBulletins();
        Finish();
    }

    // The starbase's resource units are the group's: split equally between
    // the players in the game, the starter keeping what doesn't divide.
    private void ShareTheGift()
    {
        uint gained = Starter.Ship.Hold.ResourceUnits - _unitsBefore;
        var others = _world.Players.Where(p => p.Present && p != Starter).ToList();
        if (others.Count == 0 || gained == 0)
            return;
        uint share = gained / (uint)(others.Count + 1);
        foreach (var p in others)
            p.Ship.Hold.ResourceUnits += share;
        Starter.Ship.Hold.ResourceUnits -= share * (uint)others.Count;
    }

    private void Finish()
    {
        Step = VisitStep.Done;
        Talk = null;
    }

    /// <summary>The shared story fields the visit's talks wrote, with what each was when the visit started.</summary>
    public IReadOnlyDictionary<string, int> WrittenBefore()
    {
        var written = new Dictionary<string, int>();
        foreach (var talk in Talks)
            foreach (var e in talk.Events.OfType<StateSet>())
                if (!StoryState.NotShared.ContainsKey(e.Field))
                    written[e.Field] = StoryBefore.Fields.GetValueOrDefault(e.Field);
        return written;
    }

    /// <summary>The story as it would be without the visit: what its talks wrote put back.</summary>
    public StoryState.Saved StoryWithout()
    {
        var story = StoryState.Restore(_world.Story!.Rules, _world.Story.Save());
        foreach (var (field, value) in WrittenBefore())
            story.Set(field, value);
        return story.Save();
    }

    /// <summary>The date without the visit: the two weeks, if they've passed, taken back.</summary>
    public (int Year, int Month, int Day) DateWithout()
    {
        var c = _world.Clock;
        var d = new DateTime(c.Year, c.Month, c.Day).AddDays(_skipped ? -LostDays : 0);
        return (d.Year, d.Month, d.Day);
    }

    /// <summary>
    /// The starter disconnected from the Ilwrath's fight (a retreat at once,
    /// decision 10): its part of the visit is taken back now rather than
    /// when the visit is: its hold as before the visit but with the crew it
    /// left the fight with (a disconnect keeps its crew, decision 10, and
    /// the fight's losses aren't refunded, whether the fight is then won or
    /// the visit taken back), and its fuel as before it less the retreat's 5
    /// units (to no lower than 0), which taking the visit back doesn't give
    /// back (decision 13). It's settled (<see cref="StarterSettled"/>):
    /// neither <see cref="Undo"/> nor a save puts its hold, fuel or escape
    /// counter back again, so what it does once back in the game, outside
    /// the fight, stays done. The visit itself goes on with the fight.
    /// </summary>
    internal void SettleStarter()
    {
        if (StarterSettled)
            return;
        Starter.Ship.Hold.Restore(HoldBefore with { Crew = Starter.Ship.Hold.Crew });
        Starter.Ship.Flight.Fuel = FuelBefore > GameWorld.RetreatFuelCost ? FuelBefore - GameWorld.RetreatFuelCost : 0;
        StarterSettled = true;
    }

    /// <summary>
    /// The opening is undone: what its talks wrote, the starter's ship and
    /// the two weeks put back. A starter wrecked in the Ilwrath's fight
    /// stays a wreck (decision 13): its hold as before the visit, but no
    /// crew; one that disconnected from it was settled as it left
    /// (<see cref="SettleStarter"/>), its fuel paid after its hold and fuel
    /// were put back, and isn't put back again.
    /// </summary>
    internal void Undo()
    {
        if (!Opening)
            throw new InvalidOperationException("only the opening is taken back");
        foreach (var (field, value) in WrittenBefore())
            _world.Story!.Set(field, value);
        if (!StarterSettled)
        {
            Starter.Ship.Hold.Restore(HoldBefore);
            if (Starter.Ship.Destroyed)
                Starter.Ship.Hold.SetCrew(0);
            Starter.Ship.Flight.Fuel = FuelBefore;
        }
        if (!StarterSettled && Starter.Ship.Ip is { } ip)
            ip.EscapeCounter = 0;   // none before the visit: a ship with one can't touch the starbase (a settled one's is its own, from leaving the fight)
        _world.CommanderMet = MetBefore;
        var (y, m, d) = DateWithout();
        (_world.Clock.Year, _world.Clock.Month, _world.Clock.Day) = (y, m, d);
        _skipped = false;
        Undone = true;
        Finish();
    }
}
