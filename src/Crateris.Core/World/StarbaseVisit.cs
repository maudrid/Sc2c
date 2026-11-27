// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/starbase.c's VisitStarBase
// and DoTimePassage; src/uqm/comm.c's InitCommunication, which tracks the
// talk's race's sphere and ends a hostile talk in EncounterBattle), Copyright Paul Reiche, Fred Ford and Toys for Bob,
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

    /// <summary>
    /// Two weeks pass, the screen fading to black over two seconds
    /// (DoTimePassage: a <see cref="TimeSkip"/>); a story pause's skip too
    /// (<see cref="StoryPause"/>, stage 3c item 2).
    /// </summary>
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

    /// <summary>
    /// Not a visit's: a meeting's talk with a race met in space (stage 3b-1
    /// item 10), as a talk's view carries it (<see cref="Comm.TalkView.Of(Meeting?)"/>):
    /// an ordinary talk, its player replying. Numbered last, as
    /// <see cref="IlwrathFight"/> is.
    /// </summary>
    Meeting,

    /// <summary>
    /// Not a visit's: a story pause's talk (<see cref="StoryPause"/>, stage
    /// 3c item 2), shown to everyone, its replier replying. Numbered last.
    /// </summary>
    Story,

    /// <summary>
    /// Not a visit's: a story pause's talk over, its closing lines held on
    /// everyone's screen with no replies until its replier has gone through
    /// them (<see cref="StoryPause.HoldsClosing"/>: the Black Ur-Quan's last
    /// talk, stage 3c item 4). Numbered last.
    /// </summary>
    StoryClosing,
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
/// units, which go to whoever answers, not split (the owner's rule, 2026-11-12;
/// the players may give them on).
/// Then, once allied, the ship is docked at the starbase's menu
/// (<see cref="VisitStep.Menu"/>, DoStarBase) until it leaves: from there
/// its player talks to the commander (an ordinary talk, under the dialogue
/// lock), outfits the ship and hires crew. Touching the starbase later,
/// with nothing for the opening to tell, docks at once.
/// <para>
/// The opening (the steps before the menu) can be taken back: the story
/// fields its talks wrote (only those: another player's lander may take
/// the Lunar base meanwhile), the spheres they started tracking (the
/// Ilwrath's), the starter's ship, what the talks changed on any other
/// answerer's ship and the two weeks (with what their days' events did:
/// the clock's events, the fields they set, the crew's price, the races'
/// fleets and standings they changed; they roll no hyperspace encounter,
/// stage 3c item 2), and what any event its talks ran at once changed. A save
/// made during it is of the game without it, as UQM can't save in the
/// middle of one; and if the starter leaves with no one to take the
/// replies, or every player leaves the Ilwrath's fight with the Ilwrath
/// alive (<see cref="GameWorld"/>'s AfterTheFight), or the Ilwrath is
/// killed with nobody in the game to answer the commander, or the starter
/// is gone as that fight would start, it's undone. Once docked,
/// nothing is taken back: leaving ends the visit, as departing does.
/// </para>
/// </summary>
public sealed class StarbaseVisit : TalkSequence
{
    /// <summary>LOST_DAYS: the days the starbase's building takes.</summary>
    public const int LostDays = 14;

    /// <summary>DoTimePassage's fade: two seconds, in world frames (<see cref="TimeSkip.FadeFrames"/>).</summary>
    public const int TimePassingFrames = TimeSkip.FadeFrames;

    /// <summary>The Lunar base's gift (DeltaSISGauges (0, 0, 2500)).</summary>
    public const int StarbaseGift = 2500;

    /// <summary>
    /// SetCommIntroMode (CIM_FADE_IN_SCREEN, ONE_SECOND * 2) before the
    /// allied commander's talk after the two weeks (VisitStarBase): his
    /// screen fades in from the skip's black.
    /// </summary>
    public static readonly CommIntro AfterTheDays = new(CommIntroMode.FadeInScreen, Talk.OneSecond * 2);

    /// <summary>SetCommIntroMode (CIM_CROSSFADE_SCREEN, 0) before the commander's talks before the alliance (VisitStarBase).</summary>
    public static readonly CommIntro CommanderCrossfade = new(CommIntroMode.CrossfadeScreen, 0);

    // The two weeks (DoTimePassage), once they've begun: their fade and
    // days, and what the days' events changed, taken back with them: the
    // story fields they changed, with what each was before, the crew's
    // price (the Shofixti's return lowers it), the clock's events (queued
    // and set aside) as before them, and the races' fleets and standings
    // they changed (check_race_growth's growth and headings, a fleet gone
    // extinct, the events' shields, deaths and returns), as before them.
    // Nothing else changes the events while the opening goes on after them
    // (the allied commander's talk there is story: the clock waits). No
    // hyperspace encounter is rolled meanwhile (the stage 3c plan's
    // question 3), so the encounters have nothing to take back.
    private TimeSkip? _days;

    // How many talks the visit had as the two weeks passed: those after
    // them ran after their events (TakeBackTalkEvents).
    private int _talksBeforeDays;

    // The races whose spheres the opening's talks started tracking
    // (InitCommunication's StartSphereTracking: the Ilwrath's), with their
    // fleets as before it, to be taken back with the opening.
    private readonly Dictionary<string, RaceFleet> _trackedBefore = new();

    // STARBASE_VISITED while docked: whether the allied commander has
    // greeted this player on this visit (decision 11), as his talks leave it.
    private int _greeted;

    internal StarbaseVisit(GameWorld world, Player starter, int firstRound = 0)
        : base(world, starter, firstRound)
    {
        var story = world.Story!;
        StoryBefore = story.Save();
        HoldBefore = starter.Ship.Hold.Save();
        FuelBefore = starter.Ship.Flight.Fuel;
        MetBefore = world.CommanderMet;
        if (story.Get("STARBASE_AVAILABLE") == 0)
            StartTalk(VisitStep.Commander, "comandr", new() { ["STARBASE_VISITED"] = world.CommanderMet ? 1 : 0 },
                CommanderCrossfade);
        else
            AfterAlliance();
    }

    /// <summary>
    /// Whether the visit is still in the opening (before the menu, the
    /// Ilwrath's fight too): one at a time, and taken back if it's left
    /// unfinished (<see cref="Undone"/>). A starter leaving the fight isn't
    /// that: it settles its own part (<see cref="StarterSettled"/>) and the
    /// fight goes on.
    /// </summary>
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
    public override bool Story => Step == VisitStep.TimePassing || (Talk is not null && TalkIsStory);

    /// <summary>The two weeks (DoTimePassage), once they've begun; null before.</summary>
    public override TimeSkip? Skip => _days;

    /// <summary>World frames left of the fade while time passes.</summary>
    public int FadeLeft => Step == VisitStep.TimePassing ? _days?.FadeLeft ?? 0 : 0;

    /// <summary>The story state when the visit started.</summary>
    public StoryState.Saved StoryBefore { get; }

    /// <summary>
    /// The host's fleet without what the visit's talks gave it (the ships
    /// they added, told by slot, race and captain, any still in it): what a
    /// save during an opening visit keeps, and what its undo leaves. What
    /// else happened to the fleet meanwhile (a fleet ship destroyed in a
    /// battle, a battle's or the roster's crews, a guest's talk elsewhere
    /// giving ships) stays, as it does for the players' ships.
    /// </summary>
    public IEnumerable<FleetShip> FleetWithout(IEnumerable<FleetShip> fleet)
    {
        var added = _ship?.EscortsAdded ?? Array.Empty<FleetShip>();
        return fleet.Where(s => !added.Any(a => a.Slot == s.Slot && a.Race == s.Race && a.Captain == s.Captain));
    }

    /// <summary>The starter's hold when the visit started (put back and saved with its devices as they are, not these).</summary>
    public HoldState HoldBefore { get; }

    /// <summary>The starter's fuel when the visit started.</summary>
    public uint FuelBefore { get; }

    /// <summary>Whether the group had met the commander when the visit started.</summary>
    public bool MetBefore { get; }

    // Undone (TalkSequence.Undone): its replier left with no one to take
    // the replies; every player left the Ilwrath's fight with the Ilwrath
    // alive (destroyed or disconnected); the Ilwrath was killed with nobody
    // in the game to answer the commander; or its starter was gone as that
    // fight was to start.

    /// <summary>
    /// Whether the starter's part of the visit is settled: it disconnected
    /// from the Ilwrath's fight, and its hold and fuel were put back as
    /// they were before the visit, but for the crew it left with and less
    /// the retreat's fuel, there and then (<see cref="SettleStarter"/>).
    /// From then on its ship is its own again: what it does after (back in
    /// the game, outside the fight) is neither taken back with the visit
    /// nor left out of a save.
    /// </summary>
    public bool StarterSettled { get; private set; }

    /// <summary>A world frame: the fade while time passes, and the days at its end.</summary>
    internal void Frame()
    {
        if (Step != VisitStep.TimePassing || !_days!.Frame(_world))
            return;
        // The game lost during the days (the Kohr-Ah's genocide, stage 3c item 4): VisitStarBase returns at once
        // ("You are now dead!", starbase.c:494): no commander, the visit left where it is (the game's end takes over).
        if (_world.GameOver || _world.KohrAhVictorious)
            return;
        // MoveGameClockDays has run: each day's events, no encounter
        // rolled, and the day starts afresh (tick_count = day_in_ticks).
        // What they changed is taken back with the two weeks (TakeBackDays).
        _talksBeforeDays = Talks.Count;
        // The allied commander, at the starbase (GLOBAL_FLAGS_AND_DATA ~0);
        // whether he has greeted this player on this visit is theirs, and
        // the visit is new. His screen fades in from the skip's black.
        StartTalk(VisitStep.StarbaseReady, "starbas", new() { ["GLOBAL_FLAGS_AND_DATA"] = 0xFF }, AfterTheDays);
    }

    // A talk of the visit, story or not by its rule (decision 6).
    private void StartTalk(VisitStep step, string script, Dictionary<string, int> own, CommIntro? intro = null) =>
        StartTalk(step, script, own, Comm.TalkRules.IsStory(script, _world.Story!), intro);

    private protected override void BeforeTalk(string script)
    {
        // InitCommunication: StartSphereTracking for the talk's race
        // (RACE_SHIP_FOR_COMM): the Ilwrath's sphere is known from their
        // talk on; the commander's race is the humans', who have none.
        string race = script switch
        {
            "ilwrath" => "ILWRATH_SHIP",
            "comandr" or "starbas" => "HUMAN_SHIP",
            _ => throw new ArgumentException($"no race for {script}'s talk", nameof(script)),
        };
        var fleetBefore = _world.Story!.Fleet(race);
        _world.Story.StartSphereTracking(race);
        if (Opening && _world.Story.Fleet(race) != fleetBefore)
            _trackedBefore.TryAdd(race, fleetBefore);
    }

    private protected override void TalkEnded()
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
        StartTalk(VisitStep.Alliance, "comandr", new() { ["STARBASE_VISITED"] = _world.CommanderMet ? 1 : 0 },
            CommanderCrossfade);
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
            _days = new TimeSkip(LostDays);
        }
        else
            Dock();
    }

    // DoStarBase: the menu, with the commander's talk over.
    private void Dock()
    {
        Step = VisitStep.Menu;
        // Docked, the starter answers its own menu's talks (its replies may
        // have passed to another player while it was away, and it came back).
        Replier = Starter;
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

    /// <summary>
    /// The story as it would be without the visit: what its talks wrote
    /// put back, the spheres they started tracking untracked
    /// (<see cref="TakeBackTracking"/>), and what the two weeks' events
    /// did, if they've passed, and the events its talks ran at once
    /// (<see cref="TakeBackEvents"/>).
    /// </summary>
    public StoryState.Saved StoryWithout()
    {
        var story = StoryState.Restore(_world.Story!.Rules, _world.Story.Save());
        TakeBackEvents(story);
        TakeBackTracking(story);
        foreach (var (field, value) in WrittenBefore())
            story.Set(field, value);
        return story.Save();
    }

    /// <summary>
    /// The two weeks' events taken back from a story, if the two weeks
    /// have passed: the fields they changed as before them, the crew's
    /// price, the clock's events (queued and set aside) as before them, and
    /// the races' fleets and standings they changed as before them.
    /// </summary>
    private void TakeBackDays(StoryState story) => _days?.TakeBackFrom(story);

    // Whether the two weeks have passed.
    private bool Skipped => _days is { Skipped: true };

    /// <summary>
    /// The two weeks' events and those the opening's talks ran at once
    /// (<see cref="Talk.RanAtOnce"/>) taken back from a story, the last
    /// first: the talks' after the two weeks, the two weeks', the talks'
    /// before them. UQM's opening talks (comandr.c, starbas.c, the
    /// Ilwrath's at the starbase) run none; taken back all the same, as
    /// anything the opening's talks did is.
    /// </summary>
    private void TakeBackEvents(StoryState story)
    {
        int split = Skipped ? _talksBeforeDays : Talks.Count;
        void TalksFrom(int first, int end)
        {
            for (int t = end - 1; t >= first; --t)
                foreach (var ran in Talks[t].RanAtOnce.Reverse())
                    ran.TakeBackFrom(story);
        }
        TalksFrom(split, Talks.Count);
        TakeBackDays(story);
        TalksFrom(0, split);
    }

    /// <summary>
    /// The spheres the opening's talks started tracking, as unknown again:
    /// the known strength and place as before them (whatever else the
    /// fleet did meanwhile kept).
    /// </summary>
    private void TakeBackTracking(StoryState story)
    {
        foreach (var (race, before) in _trackedBefore)
            story.SetFleet(race, story.Fleet(race) with
            {
                KnownStrength = before.KnownStrength, KnownLocX = before.KnownLocX, KnownLocY = before.KnownLocY,
            });
    }

    /// <summary>The date without the visit: the two weeks, if they've passed, taken back.</summary>
    public (int Year, int Month, int Day) DateWithout() =>
        _days?.DateWithout(_world.Clock) ?? (_world.Clock.Year, _world.Clock.Month, _world.Clock.Day);

    /// <summary>
    /// A player's hold and fuel as they'd be without the visit's talks:
    /// what the talks changed on its ship taken back (the starter's, until
    /// settled, is put back whole instead: <see cref="HoldBefore"/>).
    /// </summary>
    internal (HoldState Hold, uint Fuel) WithoutTalks(Player player, HoldState hold, uint fuel) =>
        _ship?.Changes.GetValueOrDefault(player) is { } changes
            // A lander out, saved as back aboard, stays the ship's, as Undo leaves it (TalkChanges.TakeBackFrom).
            ? changes.Without(hold, fuel, player.Ship.Hold.CrewCapacity,
                player.Ship.Hold.LanderOut is not null && player.Ship.Lander is not { Crew: <= 0 } ? 1 : 0,
                // The lander's crew in the saved hold (as back aboard), not those the talks' crew comes from.
                player.Ship.Hold.LanderOut is not null && player.Ship.Battle is null ? Math.Max(0, hold.Crew - player.Ship.Hold.Crew) : 0)
            : (hold, fuel);

    // The starter's devices as they are (stage 3c item 5, review round 1): no talk of a visit changes one, but
    // the starter may have left them in a container meanwhile; putting its hold back mustn't bring them back.
    private int[]? LiveDevices => Starter.Ship.Hold.Save().Devices;

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
        Starter.Ship.Hold.Restore(HoldBefore with { Crew = Starter.Ship.Hold.Crew, Devices = LiveDevices });
        Starter.Ship.Flight.Fuel = FuelBefore > GameWorld.RetreatFuelCost ? FuelBefore - GameWorld.RetreatFuelCost : 0;
        _ship?.Forget(Starter);   // put back already (but the crew it left with), not to be taken back again
        StarterSettled = true;
    }

    /// <summary>
    /// The opening is undone: what its talks wrote, the starter's ship,
    /// what the talks changed on any other answerer's ship, the ships the
    /// talks gave the host's fleet (only those: <see cref="FleetWithout"/>),
    /// the spheres the talks started tracking (<see cref="TakeBackTracking"/>),
    /// and the two weeks put back, with what their days' events did (the
    /// clock's events, the fields they set, the crew's price and the races'
    /// fleets and standings they changed, <see cref="TakeBackEvents"/>, which
    /// takes back the events the talks ran at once too; the days rolled no
    /// encounter, stage 3c item 2).
    /// A starter wrecked in the Ilwrath's fight
    /// stays a wreck (decision 13): its hold as before the visit, but no
    /// crew and its devices as they are (one left in a container stays
    /// there, stage 3c item 5); one that disconnected from it was settled as it left
    /// (<see cref="SettleStarter"/>: its hold as before the visit with the
    /// crew it left with, its fuel as before it less the retreat's 5
    /// units), and isn't put back again.
    /// </summary>
    internal void Undo()
    {
        if (!Opening)
            throw new InvalidOperationException("only the opening is taken back");
        TakeBackEvents(_world.Story!);
        TakeBackTracking(_world.Story!);
        foreach (var (field, value) in WrittenBefore())
            _world.Story!.Set(field, value);
        _world.Fleet.Restore(_world.Story!.Rules, FleetWithout(_world.Fleet.Ships).ToList());
        if (!StarterSettled)
        {
            Starter.Ship.Hold.Restore(HoldBefore with { Devices = LiveDevices });
            if (Starter.Ship.Destroyed)
                Starter.Ship.Hold.SetCrew(0);
            Starter.Ship.Flight.Fuel = FuelBefore;
        }
        // Every other ship the talks used (they answered after the starter
        // left, or the starter once settled): what the talks changed on it
        // taken back, and nothing else it did meanwhile.
        foreach (var (player, changes) in _ship?.Changes ?? new Dictionary<Player, TalkChanges>())
            if (player != Starter || StarterSettled)
                changes.TakeBackFrom(player.Ship);
        if (!StarterSettled && Starter.Ship.Ip is { } ip)
            ip.EscapeCounter = 0;   // none before the visit: a ship with one can't touch the starbase (a settled one's is its own, from leaving the fight)
        _world.CommanderMet = MetBefore;
        var (y, m, d) = DateWithout();
        (_world.Clock.Year, _world.Clock.Month, _world.Clock.Day) = (y, m, d);
        _days?.Forget();
        Undone = true;
        Finish();
    }
}
