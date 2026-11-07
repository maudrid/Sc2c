// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json.Nodes;
using Crateris.Core.Data;
using Crateris.Core.Net;
using Crateris.Core.Story;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>The story state (docs/plans/stage-3.md, item 1): UQM's game-state bits and what goes beside them.</summary>
[Trait("Topic", "story")]
public class StoryStateTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");
    private static readonly StoryRules Rules = StoryRules.Load(DataDir);

    private static GameClock StartClock() => new(2155, 2, 17);

    private static StoryState NewStory() => StoryState.NewGame(Rules, StartClock());

    // ---- the bits

    [Fact]
    public void TheTableIsUqmsFieldsInTheirOrder()
    {
        var t = Rules.Table;
        Assert.Equal(447, t.Fields.Count);
        Assert.Equal(1238, t.TotalBits);
        Assert.Equal(155, t.Bytes);
        Assert.Equal(new GameStateField("SHOFIXTI_VISITS", 0, 0, 3), t.Fields[0]);
        Assert.Equal(1230, t["SAMATRA_GRPOFFS3"].StartBit);
        foreach (string name in StoryState.NotShared.Keys)
            Assert.True(t.Contains(name), name);
    }

    // A bit at a time, independently of UQM's shifts and masks.
    private static int Reference(ReadOnlySpan<byte> bits, GameStateField f)
    {
        int v = 0;
        for (int i = 0; i < f.Bits; ++i)
            v |= ((bits[(f.StartBit + i) >> 3] >> ((f.StartBit + i) & 7)) & 1) << i;
        return v;
    }

    [Fact]
    public void EveryFieldHoldsItsValuesWithoutTouchingAnyOther()
    {
        var story = NewStory();
        var shared = Rules.Table.Fields.Where(f => !StoryState.NotShared.ContainsKey(f.Name)).ToList();
        var before = shared.ToDictionary(f => f.Name, f => story.Get(f.Name));
        var r = new Random(3);
        foreach (var f in shared)
        {
            foreach (int value in new[] { f.Max, 0, r.Next(f.Max + 1) })
            {
                story.Set(f.Name, value);
                Assert.Equal(value, story.Get(f.Name));
                Assert.Equal(value, Reference(story.Bits, f));
                before[f.Name] = value;
            }
            // Every field so far keeps its last value.
            foreach (var g in shared.Where(g => g.Index <= f.Index + 1))
                Assert.Equal(before[g.Name], story.Get(g.Name));
        }
        foreach (var f in shared)
            Assert.Equal(before[f.Name], Reference(story.Bits, f));
    }

    [Fact]
    public void AValueTooBigRunsIntoTheNextBitsAsUqmsDoes()
    {
        // setGameState shifts the value into the field's byte without masking it:
        // SHOFIXTI_VISITS (bits 0 to 2) set to 0xFF also sets bits 3 to 7.
        var story = NewStory();
        story.Set("SHOFIXTI_VISITS", 0xFF);
        Assert.Equal(7, story.Get("SHOFIXTI_VISITS"));
        Assert.Equal(3, story.Get("SHOFIXTI_STACK1"));     // bits 3 and 4
        Assert.Equal(0xFF, story.Bits[0]);
        // Only a byte is taken, as UQM's BYTE parameter takes it.
        var other = NewStory();
        other.Set("SHOFIXTI_VISITS", 0x101);
        Assert.Equal(1, other.Get("SHOFIXTI_VISITS"));
        Assert.Equal(0, other.Get("SHOFIXTI_STACK1"));
    }

    [Fact]
    public void ThirtyTwoBitValuesAreFourFieldsLowByteFirst()
    {
        var story = NewStory();
        story.Set32("URQUAN_PROBE_GRPOFFS0", 0x12345678);
        Assert.Equal(0x12345678u, story.Get32("URQUAN_PROBE_GRPOFFS0"));
        Assert.Equal(0x78, story.Get("URQUAN_PROBE_GRPOFFS0"));
        Assert.Equal(0x12, story.Get("URQUAN_PROBE_GRPOFFS3"));
    }

    [Fact]
    public void FieldsKeptElsewhereAndUnknownOnesCantBeUsedHere()
    {
        var story = NewStory();
        var e = Assert.Throws<InvalidOperationException>(() => story.Set("STARBASE_VISITED", 1));
        Assert.Contains("each player's own", e.Message);
        Assert.Throws<InvalidOperationException>(() => story.Get("LANDER_SHIELDS"));
        Assert.Throws<InvalidOperationException>(() => story.Get("MELNORME_CREDIT0"));
        Assert.Throws<InvalidOperationException>(() => story.Get("ARILOU_SPACE_SIDE"));
        Assert.Throws<InvalidOperationException>(() => story.Set("PLANETARY_LANDING", 1));
        Assert.Equal(0, story.Get("PORTAL_COUNTER"));   // the portal is the group's
        Assert.Throws<KeyNotFoundException>(() => story.Get("NO_SUCH_STATE"));
    }

    [Fact]
    public void TheVersionMovesOnlyWhenSomethingChanges()
    {
        var story = NewStory();
        long v = story.Version;
        story.Set("STARBASE_AVAILABLE", 0);
        Assert.Equal(v, story.Version);
        story.Set("STARBASE_AVAILABLE", 1);
        Assert.Equal(v + 1, story.Version);
        story.SetAlliance("HUMAN_SHIP", Allied.Good);
        story.SetCrewCost(story.CrewCost);
        Assert.Equal(v + 1, story.Version);
        story.SetAlliance("HUMAN_SHIP", Allied.Bad);
        Assert.Equal(v + 2, story.Version);
    }

    [Fact]
    public void TheSystemsReadTheirTwoFieldsFromIt()
    {
        var story = NewStory();
        Assert.Equal(SystemStoryState.NewGame, story.SystemStory);
        story.Set("CHMMR_UNLEASHED", 1);
        story.Set("SPATHI_SHIELDED_SELVES", 1);
        Assert.Equal(new SystemStoryState(ChmmrUnleashed: true, SpathiShieldedSelves: true), story.SystemStory);
    }

    // ---- a new game

    [Fact]
    public void ANewGameIsUqmsInitGameStructuresAndFirstEvents()
    {
        var story = NewStory();
        // Every field is 0 but the one SLYLANDRO_RAMP_UP sets, which AddInitialGameEvents runs at once.
        foreach (var f in Rules.Table.Fields.Where(f => !StoryState.NotShared.ContainsKey(f.Name)))
            Assert.Equal(f.Name == "SLYLANDRO_MULTIPLIER" ? 1 : 0, story.Get(f.Name));
        Assert.Equal(3, story.CrewCost);
        Assert.Equal(20, story.FuelCost);
        Assert.Equal(10, story.ModuleCost("PLANET_LANDER"));
        Assert.Equal(40, story.ModuleCost("CREW_POD"));
        Assert.Equal(15, story.ModuleCost("STORAGE_BAY"));
        Assert.Equal(0, story.ModuleCost("BLASTER_WEAPON"));
        Assert.Equal(50, Rules.ModuleCostScale);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 8, 25 }, story.ElementWorth);
        Assert.Equal(24, story.Alliances.Count);
        // Every race not allied but the humans (SetRaceAllied (HUMAN_SHIP, TRUE)).
        for (int i = 0; i < Rules.Races.Count; ++i)
            Assert.Equal(Rules.Races[i] == "HUMAN_SHIP" ? Allied.Good : Allied.Bad, story.Alliances[i]);
        // The day after the start, the Arilou's date, 182 days on (the
        // Slylandro, added when it ran) and four years on, in date order.
        Assert.Equal(new[]
        {
            new StoryEvent("HYPERSPACE_ENCOUNTER_EVENT", 2155, 2, 18),
            new StoryEvent("ARILOU_ENTRANCE_EVENT", 2155, 3, 17),
            new StoryEvent("SLYLANDRO_RAMP_UP", 2155, 8, 18),
            new StoryEvent("KOHR_AH_VICTORIOUS_EVENT", 2159, 2, 17),
        }, story.Events);
        Assert.Equal(0, story.Version);
    }

    // ---- events

    [Fact]
    public void EventsGoInDateOrderAndOneDaysInTheOrderAdded()
    {
        var story = NewStory();
        var clock = new GameClock(2155, 12, 30);
        Assert.True(story.Events.Add(true, 0, 3, 0, "ZOQFOT_DISTRESS_EVENT", clock));       // over the year's end
        Assert.True(story.Events.Add(false, 1, 2, 2156, "ZOQFOT_DEATH_EVENT", clock));       // the same day, after it
        Assert.True(story.Events.Add(true, 13, 0, 0, "SPATHI_SHIELD_EVENT", clock));        // 13 months on
        Assert.False(story.Events.Add(false, 12, 29, 2155, "YEHAT_REBEL_EVENT", clock));     // yesterday
        Assert.True(story.Events.Add(false, 12, 30, 2155, "YEHAT_REBEL_EVENT", clock));      // today
        var at = story.Events.ToList();
        Assert.Equal(new StoryEvent("YEHAT_REBEL_EVENT", 2155, 12, 30), at[3]);
        Assert.Equal(new StoryEvent("ZOQFOT_DISTRESS_EVENT", 2156, 1, 2), at[4]);
        Assert.Equal(new StoryEvent("ZOQFOT_DEATH_EVENT", 2156, 1, 2), at[5]);
        Assert.Equal(new StoryEvent("SPATHI_SHIELD_EVENT", 2157, 1, 30), at[6]);
        Assert.Equal("KOHR_AH_VICTORIOUS_EVENT", at[7].Event);
        Assert.Throws<KeyNotFoundException>(() => story.Events.Add(true, 0, 1, 0, "NO_SUCH_EVENT", clock));
    }

    [Fact]
    public void TheQueueHoldsUqmsNumberOfEvents()
    {
        var story = NewStory();
        var clock = StartClock();
        while (story.Events.Count < story.Events.MaxEvents)
            Assert.True(story.Events.Add(true, 0, 5, 0, "ARILOU_EXIT_EVENT", clock));
        Assert.False(story.Events.Add(true, 0, 5, 0, "ARILOU_EXIT_EVENT", clock));
        Assert.Equal(18, story.Events.MaxEvents);    // NUM_EVENTS
        Assert.Throws<ArgumentOutOfRangeException>(() => story.Events.Add(true, -1, 0, 0, "ARILOU_EXIT_EVENT", clock));
    }

    [Fact]
    public void TheSlylandroRampUpRunsFourTimesThenStops()
    {
        var story = NewStory();
        var clock = StartClock();
        for (int i = 2; i <= 4; ++i)
        {
            story.RunEvent("SLYLANDRO_RAMP_UP", clock);
            Assert.Equal(i, story.Get("SLYLANDRO_MULTIPLIER"));
        }
        int queued = story.Events.Count;
        story.RunEvent("SLYLANDRO_RAMP_UP", clock);
        Assert.Equal(4, story.Get("SLYLANDRO_MULTIPLIER"));
        Assert.Equal(queued, story.Events.Count);
        // The others wait for stage 3c.
        Assert.Throws<NotSupportedException>(() => story.RunEvent("ARILOU_ENTRANCE_EVENT", clock));
    }

    // ---- prices

    [Fact]
    public void BuyingAThousandCrewRaisesTheirPriceByTwoAsUqmsCrewTransactionDoes()
    {
        var story = NewStory();
        story.CrewTransaction(999);
        Assert.Equal(3, story.CrewCost);
        story.CrewTransaction(1);
        Assert.Equal(5, story.CrewCost);
        Assert.Equal(1000, story.CrewPurchased);
        story.CrewTransaction(500);
        Assert.Equal(5, story.CrewCost);
        story.CrewTransaction(-501);
        Assert.Equal(3, story.CrewCost);
        Assert.Equal(999, story.CrewPurchased);
        story.CrewTransaction(-5000);           // never below none
        Assert.Equal(0, story.CrewPurchased);
        // CrewCost is a byte: its rise wraps, as UQM's does.
        story.SetCrewCost(254);
        story.CrewTransaction(1000);
        Assert.Equal(0, story.CrewCost);
        story.CrewTransaction(-1);
        Assert.Equal(254, story.CrewCost);
        story.SetCrewCost(3);
        story.CrewTransaction(-999);
        Assert.Equal(0, story.CrewPurchased);
        // With the Shofixti allied the count stands still (the price still moves).
        story.SetAlliance("SHOFIXTI_SHIP", Allied.Good);
        story.CrewTransaction(1200);
        Assert.Equal(0, story.CrewPurchased);
        Assert.Equal(5, story.CrewCost);
    }

    [Fact]
    public void PricesAreBytesAndModulesAreUqms()
    {
        var story = NewStory();
        story.SetModuleCost("BLASTER_WEAPON", 120);
        Assert.Equal(120, story.ModuleCost("BLASTER_WEAPON"));
        Assert.Throws<ArgumentOutOfRangeException>(() => story.SetModuleCost("GUN_WEAPON", 256));
        Assert.Throws<KeyNotFoundException>(() => story.SetModuleCost("WARP_DRIVE", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => story.SetCrewCost(-1));
        Assert.Throws<KeyNotFoundException>(() => story.Alliance("ANDROSYNTH_SHIP"));
    }

    // ---- saving

    private static StoryState Busy()
    {
        var story = NewStory();
        story.Set("STARBASE_AVAILABLE", 1);
        story.Set("NEW_ALLIANCE_NAME", 3);
        story.Set32("COLONY_GRPOFFS0", 0xDEADBEEF);
        story.CrewTransaction(1001);
        story.SetModuleCost("PLANET_LANDER", 0);
        story.SetModuleCost("CANNON_WEAPON", 100);
        story.SetAlliance("HUMAN_SHIP", Allied.Good);
        story.SetAlliance("MYCON_SHIP", Allied.Dead);
        story.Events.Add(true, 2, 0, 0, "SHOFIXTI_RETURN_EVENT", StartClock());
        return story;
    }

    private static void SameStory(StoryState a, StoryState b)
    {
        Assert.Equal(a.Bits.ToArray(), b.Bits.ToArray());
        Assert.Equal(a.CrewCost, b.CrewCost);
        Assert.Equal(a.FuelCost, b.FuelCost);
        Assert.Equal(Rules.Modules.Select(a.ModuleCost), Rules.Modules.Select(b.ModuleCost));
        Assert.Equal(a.Alliances, b.Alliances);
        Assert.Equal(a.Events.ToList(), b.Events.ToList());
    }

    [Fact]
    public void ASavedStoryComesBackAsItWas()
    {
        var story = Busy();
        var saved = story.Save();
        Assert.DoesNotContain("STARBASE_VISITED", saved.Fields.Keys);
        Assert.Equal(1, saved.Fields["STARBASE_AVAILABLE"]);
        Assert.DoesNotContain("SHOFIXTI_VISITS", saved.Fields.Keys);   // fields at 0 aren't written
        SameStory(story, StoryState.Restore(Rules, saved));
    }

    [Fact]
    public void ABadSavedStoryIsRefused()
    {
        StoryState.Saved Good() => Busy().Save();
        void Refused(Action<StoryState.Saved> spoil)
        {
            var s = Good();
            spoil(s);
            Assert.Throws<InvalidDataException>(() => StoryState.Restore(Rules, s));
        }
        Refused(s => s.Fields["NO_SUCH_STATE"] = 1);
        Refused(s => s.Fields["STARBASE_AVAILABLE"] = 2);        // a 1-bit field
        Refused(s => s.Fields["NEW_ALLIANCE_NAME"] = -1);
        Refused(s => s.Fields["STARBASE_VISITED"] = 1);          // not shared
        Refused(s => s.ModuleCost["WARP_DRIVE"] = 1);
        Refused(s => s.ModuleCost["GUN_WEAPON"] = 300);
        Refused(s => s.Alliances.Remove("HUMAN_SHIP"));
        Refused(s => s.Alliances["HUMAN_SHIP"] = (Allied)7);
        Refused(s => s.Events.Reverse());
        Refused(s => s.Events.Add(new StoryEvent("ARILOU_EXIT_EVENT", 2155, 2, 30)));
        Refused(s => s.Events.Add(new StoryEvent("NO_SUCH_EVENT", 2200, 1, 1)));
        Assert.Throws<InvalidDataException>(() => StoryState.Restore(Rules, Good() with { CrewCost = 256 }));
    }

    [ContentFact]
    public void AHostSaveKeepsTheStoryAndAnOlderOneStartsANewGames()
    {
        var systems = SystemCatalog.Load(DataDir);
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir, systems.Map), "h", "Host", systems, TestContent.Shapes);
        Assert.NotNull(world.Story);
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        world.Story.SetAlliance("HUMAN_SHIP", Allied.Good);
        // The clock passes events' days in 3a (none runs until 3c): they stay queued.
        world.Clock.Month = 4;
        string json = HostSave.ToJson(world);
        var loaded = HostSave.FromJson(json, systems, TestContent.Shapes);
        SameStory(world.Story, loaded.Story!);
        Assert.Equal("HYPERSPACE_ENCOUNTER_EVENT", loaded.Story!.Events[0].Event);

        // A save from before stage 3a: a new game's story, its events counted from the
        // save's day, so the Arilou's date, already past, isn't queued.
        var node = JsonNode.Parse(json)!;
        node["version"] = 5;
        foreach (var slots in node["players"]!.AsArray())
            slots!.AsObject().Remove("modules");   // saves before 9 have no slots
        node.AsObject().Remove("story");
        node["clock"]!["year"] = 2157;
        node["clock"]!["month"] = 2;
        var old = HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes);
        Assert.Equal(new[]
        {
            new StoryEvent("HYPERSPACE_ENCOUNTER_EVENT", 2157, 2, 18),
            new StoryEvent("SLYLANDRO_RAMP_UP", 2157, 8, 18),
            new StoryEvent("KOHR_AH_VICTORIOUS_EVENT", 2161, 2, 17),
        }, old.Story!.Events);
        Assert.Equal(Allied.Good, old.Story.Alliance("HUMAN_SHIP"));
        // An older save can't have a story, and a newer one must.
        node["story"] = JsonNode.Parse(json)!["story"]!.DeepClone();
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes));
        node = JsonNode.Parse(json)!;
        node.AsObject().Remove("story");
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes));

        // A story that makes other systems than the ones loaded is refused.
        node = JsonNode.Parse(json)!;
        node["story"]!["fields"]!["CHMMR_UNLEASHED"] = 1;
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString(), systems, TestContent.Shapes));
        // And a save with a story can't be loaded without the data to read it.
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(json));
    }

    // ---- snapshots

    [Fact]
    public void ASnapshotIsMadeAgainOnlyWhenTheStoryChanges()
    {
        var world = new GameWorld(new GameSettings(), StartClock()) { Story = NewStory() };
        var a = world.Snapshot().Story;
        Assert.Same(a, world.Snapshot().Story);
        world.Story!.Set("STARBASE_AVAILABLE", 1);
        var b = world.Snapshot().Story!;
        Assert.NotSame(a, b);
        Assert.Equal(1, b.Get(Rules.Table, "STARBASE_AVAILABLE"));
        Assert.Equal(1, b.Get(Rules.Table, "SLYLANDRO_MULTIPLIER"));
        Assert.Null(new GameWorld(new GameSettings(), StartClock()).Snapshot().Story);
        // Another story state at the same version isn't mistaken for the last.
        var other = NewStory();
        other.Set("MOONBASE_ON_SHIP", 1);
        Assert.Equal(world.Story!.Version, other.Version);
        world.Story = other;
        Assert.Equal(1, world.Snapshot().Story!.Get(Rules.Table, "MOONBASE_ON_SHIP"));
    }

    private static WorldSnapshot At(long frame, StoryState? story) =>
        new(frame, 2155, 2, 17, Array.Empty<ShipSnapshot>(), Story: story is null ? null : StorySnapshot.Of(story));

    private static WorldSnapshot RoundTrip(WorldSnapshot s, WorldSnapshot? b, out int size)
    {
        var w = new WireWriter();
        SnapshotCodec.Encode(w, s, b);
        size = w.Written.Length;
        return SnapshotCodec.Decode(w.Written.ToArray(), b);
    }

    private static void SameSnapshot(StorySnapshot? a, StorySnapshot? b)
    {
        if (a is null)
        {
            Assert.Null(b);
            return;
        }
        Assert.True(a.SameAs(b));
    }

    [Fact]
    public void TheStoryGoesOnTheWireInFullAndAsTheBytesThatChanged()
    {
        var story = Busy();
        var full = At(10, story);
        var got = RoundTrip(full, null, out int fullSize);
        SameSnapshot(full.Story, got.Story);

        // Nothing changed: the delta has no story part, and the base's is kept.
        var same = full with { Frame = 11 };
        var kept = RoundTrip(same, got, out int sameSize);
        Assert.Same(got.Story, kept.Story);

        // One field changed: a few bytes.
        story.Set("MOONBASE_ON_SHIP", 1);
        var next = At(12, story);
        var changed = RoundTrip(next, got, out int changedSize);
        SameSnapshot(next.Story, changed.Story);
        Assert.True(changedSize <= sameSize + 6, $"{changedSize} bytes for one field, {sameSize} for none");
        Assert.True(fullSize > changedSize);

        // Only the prices, or only the standing, changed: no bits.
        story.CrewTransaction(1000);
        var pricier = RoundTrip(At(15, story), changed, out int priceSize);
        SameSnapshot(At(15, story).Story, pricier.Story);
        Assert.True(priceSize < fullSize);
        var before = At(16, story);
        story.SetAlliance("SPATHI_SHIP", Allied.Good);
        SameSnapshot(At(17, story).Story, RoundTrip(At(17, story), RoundTrip(before, null, out _), out _).Story);

        // No story (a world without one) after one, and back.
        var none = RoundTrip(At(13, null), got, out _);
        Assert.Null(none.Story);
        SameSnapshot(full.Story, RoundTrip(At(14, Busy()), none, out _).Story);
    }

    [Fact]
    public void RandomStoriesSurviveDeltasFromRandomBases()
    {
        var r = new Random(11);
        var fields = Rules.Table.Fields.Where(f => !StoryState.NotShared.ContainsKey(f.Name)).ToList();
        var story = NewStory();
        WorldSnapshot? last = null;
        for (int frame = 1; frame < 300; ++frame)
        {
            for (int n = r.Next(4); n > 0; --n)
            {
                var f = fields[r.Next(fields.Count)];
                story.Set(f.Name, r.Next(f.Max + 1));
            }
            if (r.Next(10) == 0)
                story.SetAlliance(Rules.Races[r.Next(Rules.Races.Count)], (Allied)r.Next(3));
            if (r.Next(10) == 0)
                story.SetModuleCost(Rules.Modules[r.Next(Rules.Modules.Count)], r.Next(256));
            var snap = At(frame, story);
            var got = RoundTrip(snap, r.Next(5) == 0 ? null : last, out _);
            SameSnapshot(snap.Story, got.Story);
            last = snap;
        }
    }

    [Fact]
    public void BadStoryDataIsRefused()
    {
        var w = new WireWriter();
        var s = At(5, NewStory());
        SnapshotCodec.Encode(w, s with { Story = null });
        // Without a story the message ends with the ship count; with one, the story
        // part comes just before it, so its kind byte is where that count was.
        int at = w.Written.Length - 1;
        SnapshotCodec.Encode(w, s);
        byte[] good = w.Written.ToArray();
        Assert.Equal(0x16, good[at]);   // present, with the prices and the standing, from zeros
        var spoilt = (byte[])good.Clone();
        spoilt[at] = 0x36;                                 // no such kind
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(spoilt));
        spoilt = (byte[])good.Clone();
        spoilt[at] = 0x17;                                 // a delta, but there's no base
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(spoilt));
        spoilt = (byte[])good.Clone();
        spoilt[^2] = 7;                                    // the last race's standing (before the ship count)
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(spoilt));
    }
}
