// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.
//
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/starbase.c's VisitStarBase
// and DoTimePassage), Copyright Paul Reiche, Fred Ford and Toys for Bob,
// Inc. 1992-2002, and the UQM contributors, under the GNU GPL 2 or later.

using Crateris.Core.Comm;
using Crateris.Core.Story;

namespace Crateris.Core.World;

/// <summary>Where a visit to Earth's starbase is (VisitStarBase's order).</summary>
public enum VisitStep
{
    /// <summary>The commander's talk before the alliance (comandr.c).</summary>
    Commander,

    /// <summary>The Ilwrath ship that answers the probe's call (ilwrathc.c); its battle counts as won (decision 3).</summary>
    Ilwrath,

    /// <summary>The commander again, impressed: the alliance and its name.</summary>
    Alliance,

    /// <summary>Two weeks pass, the screen fading to black over two seconds (DoTimePassage).</summary>
    TimePassing,

    /// <summary>The allied commander: the starbase is ready (starbas.c).</summary>
    StarbaseReady,

    /// <summary>Over.</summary>
    Done,
}

/// <summary>
/// One player's visit to Earth's starbase, as VisitStarBase runs it
/// (stage 3a, item 5). Before the alliance, the commander talks; if he
/// warns of the Ilwrath, their ship talks next and its battle counts as
/// won (decision 3, until stage 4), and the commander talks again and
/// makes the alliance. With the Lunar base aboard, two weeks pass and the
/// allied commander says the starbase is ready, with 2,500 resource
/// units, which are split between the players in the game (decision 5).
/// The starbase's menu comes with item 8; until then the visit ends there,
/// and the ship is back in flight by the starbase.
/// <para>
/// What the visit changed is kept apart from what it started from
/// (<see cref="StoryBefore"/> and the starter's ship), so a save made
/// during it is of the game before it, as UQM can't save in the middle of
/// one; and if the starter leaves, the visit is undone.
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

    internal StarbaseVisit(GameWorld world, Player starter)
    {
        _world = world;
        Starter = starter;
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
        // MoveGameClockDays: the clock's events wait for stage 3c.
        for (int i = 0; i < LostDays; ++i)
            _world.Clock.NextDay();
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
        Talk = _world.TalkLibrary!.Create(script, _world.Story!, own,
            new PlayerTalkShip(Starter, _world.TalkLibrary.Strings), _world.Clock, seed);
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
                    StartTalk(VisitStep.Ilwrath, "ilwrath", new());
                break;
            case VisitStep.Ilwrath:
                // Its battle counts as won (decision 3): on to the commander.
                StartTalk(VisitStep.Alliance, "comandr", new() { ["STARBASE_VISITED"] = _world.CommanderMet ? 1 : 0 });
                break;
            case VisitStep.Alliance:
                _world.CommanderMet = talk.OwnFields.GetValueOrDefault("STARBASE_VISITED") != 0;
                AfterAlliance();
                break;
            case VisitStep.StarbaseReady:
                ShareTheGift();
                Finish();
                break;
        }
    }

    // With the Lunar base aboard (or the Chmmr's bomb just installed, in
    // 3c), time passes and the allied commander talks; else the starbase's
    // menu, item 8's.
    private void AfterAlliance()
    {
        var story = _world.Story!;
        if (story.Get("MOONBASE_ON_SHIP") != 0 || story.Get("CHMMR_BOMB_STATE") == 2)
        {
            Step = VisitStep.TimePassing;
            Talk = null;
            _fadeLeft = TimePassingFrames;
        }
        else
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

    /// <summary>The visit is undone: the story and the starter's ship back as they were when it started.</summary>
    internal void Undo()
    {
        _world.Story = StoryState.Restore(_world.Story!.Rules, StoryBefore);
        Starter.Ship.Hold.Restore(HoldBefore);
        Starter.Ship.Flight.Fuel = FuelBefore;
        _world.CommanderMet = MetBefore;
        Finish();
    }
}
