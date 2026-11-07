// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text.Json;
using Crateris.Core.Comm;
using Crateris.Core.Data;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>The dialogue lock (docs/plans/stage-3.md, item 7): one player per alien race at a time.</summary>
[Trait("Topic", "talk")]
public class DialogueLockTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    [Fact]
    public void EveryConversationHasARaceAndOneRacesConversationsShareItsLock()
    {
        var locks = DialogueLocks.Load(DataDir);
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(DataDir, "conversations.json")));
        foreach (var c in doc.RootElement.GetProperty("conversations").EnumerateArray())
            Assert.NotNull(locks.RaceOf(c.GetProperty("conversation").GetString()!));
        Assert.Equal("ARILOU_SHIP", locks.RaceOf("ARILOU_CONVERSATION"));
        Assert.Equal("THRADDASH_SHIP", locks.RaceOf("THRADD_CONVERSATION"));
        Assert.Equal(locks.RaceOf("YEHAT_CONVERSATION"), locks.RaceOf("YEHAT_REBEL_CONVERSATION"));
        Assert.Equal(locks.RaceOf("URQUAN_CONVERSATION"), locks.RaceOf("URQUAN_DRONE_CONVERSATION"));
        Assert.Equal(locks.RaceOf("SLYLANDRO_CONVERSATION"), locks.RaceOf("SLYLANDRO_HOME_CONVERSATION"));
        Assert.Equal("HUMAN_SHIP", locks.RaceOf("COMMANDER_CONVERSATION"));
        Assert.NotEqual(locks.RaceOf("URQUAN_CONVERSATION"), locks.RaceOf("BLACKURQ_CONVERSATION"));   // the Kohr-Ah are their own
        Assert.Throws<KeyNotFoundException>(() => locks.RaceOf("INVALID_CONVERSATION"));
        // The Spathi's one id runs both its scripts (init_race): one lock.
        var spathi = ConversationScripts.Load(DataDir).For("SPATHI_CONVERSATION");
        Assert.Equal(2, spathi.Count);
    }

    [Fact]
    public void ASecondPlayerIsToldWhoIsTalkingUntilTheTalkEnds()
    {
        var locks = DialogueLocks.Load(DataDir);
        Assert.True(locks.TryTake("YEHAT_CONVERSATION", "a", out var holder));
        Assert.Equal("a", holder);
        Assert.False(locks.TryTake("YEHAT_REBEL_CONVERSATION", "b", out holder));   // the same race
        Assert.Equal("a", holder);
        Assert.Equal("a", locks.Holder("YEHAT_CONVERSATION"));
        Assert.True(locks.TryTake("YEHAT_CONVERSATION", "a", out _));            // theirs already
        Assert.True(locks.TryTake("SPATHI_CONVERSATION", "b", out _));           // another race
        locks.Release("YEHAT_CONVERSATION", "b");                                 // not b's to let go
        Assert.Equal("a", locks.Holder("YEHAT_CONVERSATION"));
        locks.Release("YEHAT_REBEL_CONVERSATION", "a");
        Assert.Null(locks.Holder("YEHAT_CONVERSATION"));
        Assert.True(locks.TryTake("YEHAT_CONVERSATION", "b", out _));
        locks.ReleaseAll("b");
        Assert.Empty(locks.Held);
    }

    [Fact]
    public void APlayerLeavingLetsGoOfTheirLocks()
    {
        var world = GameWorld.NewGame(new GameSettings(), NewGameData.Load(DataDir), "host", "Host");
        world.DialogueLocks = DialogueLocks.Load(DataDir);
        Assert.Equal(JoinResult.Joined, world.Join("guest", "Guest"));
        Assert.True(world.DialogueLocks.TryTake("COMMANDER_CONVERSATION", "guest", out _));
        Assert.True(world.DialogueLocks.TryTake("ORZ_CONVERSATION", "host", out _));
        world.Leave("guest");
        Assert.Null(world.DialogueLocks.Holder("COMMANDER_CONVERSATION"));
        Assert.Equal("host", world.DialogueLocks.Holder("ORZ_CONVERSATION"));
    }
}
