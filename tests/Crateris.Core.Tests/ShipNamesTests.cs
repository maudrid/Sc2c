// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using System.Text;
using System.Text.Json.Nodes;
using Crateris.Core.Content;
using Crateris.Core.Data;
using Crateris.Core.Net;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

/// <summary>Captain and ship names (docs/plans/stage-3.md, item 2 and decision 10).</summary>
[Trait("Topic", "world")]
public class ShipNamesTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static GameWorld NewWorld(GameSettings? settings = null) =>
        GameWorld.NewGame(settings ?? new GameSettings(), NewGameData.Load(DataDir), "host-id", "Host");

    [Fact]
    public void NamesAreCutToUqmsFifteenBytesNeverMidCharacter()
    {
        Assert.Equal("Vindicator", ShipNames.Fit("Vindicator"));
        Assert.Equal("", ShipNames.Fit(""));                                  // UQM's text entry allows it
        Assert.Equal("abcdefghijklmno", ShipNames.Fit("abcdefghijklmnopqrst"));
        Assert.Equal("ÅÅÅÅÅÅÅ", ShipNames.Fit("ÅÅÅÅÅÅÅÅ"));                    // 2 bytes each: 14, not 16
        Assert.Equal("abc🚀🚀🚀", ShipNames.Fit("abc🚀🚀🚀🚀"));                  // 3 + 12 = 15
        Assert.Equal("ab🚀🚀🚀", ShipNames.Fit("ab🚀🚀🚀🚀"));
        foreach (string s in new[] { "船船船船船船", "x" + new string('é', 20), "🚀🚀🚀🚀🚀" })
            Assert.True(Encoding.UTF8.GetByteCount(ShipNames.Fit(s)) <= ShipNames.MaxBytes, s);
        // As typed into DoTextEntry: a character that doesn't fit is turned away, a shorter one after it still goes in.
        Assert.Equal("aaaaaaaaaaaaaab", ShipNames.Fit("aaaaaaaaaaaaaa🚀b"));
        // A base letter keeps its place and its accent goes when it doesn't fit, as by code point in UQM.
        Assert.Equal("aaaaaaaaaaaaaae", ShipNames.Fit("aaaaaaaaaaaaaae\u0301"));
    }

    [Fact]
    public void OnlyCharactersUqmsTextEntryTakesAreKept()
    {
        Assert.Equal("Bob Smith", ShipNames.Fit("Bob\n\t Smith\u0007"));   // controls (C0) out, the space in
        Assert.Equal("ab", ShipNames.Fit("a\u0085\u00A0b"));               // C1 controls and 0xA0 out (UniChar_isGraph)
        Assert.Equal("ab", ShipNames.Fit("a\uE000\uF8FFb"));               // the private use area out
        Assert.Equal("a\u00A1\uF900b", ShipNames.Fit("a\u00A1\uF900b"));  // just past either end in
        Assert.Equal("ab", ShipNames.Fit("a\uD800b"));                      // a lone surrogate isn't text
        Assert.Equal("ab", ShipNames.Fit("a\uDC00b\uD800"));
        Assert.Equal("a\u007Eb", ShipNames.Fit("a\u007E\u007Fb"));        // DEL out
    }

    [ContentFact]
    public void TheFlagshipsDefaultsAreUqmsGameStrings()
    {
        var strings = TestContent.With(GameStrings.Load);
        Assert.Equal("Vindicator", ShipNames.FlagshipDefault(strings));     // NAMING_STRING_BASE + 2
        Assert.Equal("Zelnick", ShipNames.CommanderDefault(strings));        // NAMING_STRING_BASE + 3
        Assert.Equal("Vindicator", ShipNames.Ship(ShipKind.Flagship, null, strings));
        Assert.Equal("Zelnick", ShipNames.Captain(ShipKind.Flagship, null, "Host", strings));
        Assert.Equal("Bob", ShipNames.Captain(ShipKind.Escort, null, "Bob", strings));
        Assert.Equal("Bobbington-Smyt", ShipNames.Captain(ShipKind.Escort, null, "Bobbington-Smythe", strings));
        Assert.Equal("Rex", ShipNames.Captain(ShipKind.Flagship, "Rex", "Host", strings));
    }

    [Fact]
    public void AGuestsEscortIsNamedFromTheSettingsAndItsCaptainByThePlayer()
    {
        var world = NewWorld(new GameSettings { EscortShipName = "A very long escort name" });
        world.Join("g", "Guest");
        var guest = world.Find("g")!;
        Assert.Equal("A very long esc", guest.Ship.Name);
        Assert.Null(guest.CaptainName);
        Assert.Null(world.Host.Ship.Name);           // UQM's, from the content
        Assert.Null(world.Host.CaptainName);
        var snap = world.Snapshot().Ships.Single(s => s.Id == "g");
        Assert.Equal("A very long esc", snap.ShipName);
        Assert.Null(snap.CaptainName);
    }

    [Fact]
    public void RenamingSetsEitherOrBothAndGoesOnTheWire()
    {
        var world = NewWorld();
        world.Join("g", "Guest");
        Assert.True(world.Rename("g", "Starlight Express", null));
        Assert.Equal("Starlight Expre", world.Find("g")!.Ship.Name);
        Assert.Null(world.Find("g")!.CaptainName);
        Assert.True(world.Rename("host-id", null, "Hayes"));
        Assert.Equal("Hayes", world.Host.CaptainName);
        Assert.Null(world.Host.Ship.Name);
        Assert.False(world.Rename("nobody", "x", "y"));

        var w = new WireWriter();
        var before = world.Snapshot();
        SnapshotCodec.Encode(w, before);
        var got = SnapshotCodec.Decode(w.Written.ToArray());
        Assert.Equal("Hayes", got.Ships.Single(s => s.Id == "host-id").CaptainName);
        Assert.Equal("Starlight Expre", got.Ships.Single(s => s.Id == "g").ShipName);
        // A delta carries a rename.
        world.Rename("g", null, "Captain G");
        world.Step();
        var next = world.Snapshot();
        SnapshotCodec.Encode(w, next, before);
        var delta = SnapshotCodec.Decode(w.Written.ToArray(), got);
        Assert.Equal("Captain G", delta.Ships.Single(s => s.Id == "g").CaptainName);
        Assert.Equal("Starlight Expre", delta.Ships.Single(s => s.Id == "g").ShipName);
    }

    [Fact]
    public void ADeltaCarriesNamesGoingBackToNone()
    {
        var world = NewWorld();
        var snap = world.Snapshot();
        var named = snap with { Ships = snap.Ships.Select(sh => sh with { ShipName = "Rocinante", CaptainName = "Holden" }).ToArray() };
        var w = new WireWriter();
        SnapshotCodec.Encode(w, named);
        var got = SnapshotCodec.Decode(w.Written.ToArray());
        var none = snap with { Frame = snap.Frame + 1 };
        SnapshotCodec.Encode(w, none, named);
        var back = SnapshotCodec.Decode(w.Written.ToArray(), got).Ships.Single();
        Assert.Null(back.ShipName);
        Assert.Null(back.CaptainName);
    }

    [Theory]
    [InlineData("sixteen bytes!!!")]
    [InlineData("tab\there")]
    [InlineData("\uE000")]
    public void ANameUqmCouldntHoldIsRefusedOnTheWire(string bad)
    {
        var world = NewWorld();
        var snap = world.Snapshot();
        var s = snap with { Ships = snap.Ships.Select(sh => sh with { ShipName = bad }).ToArray() };
        var w = new WireWriter();
        SnapshotCodec.Encode(w, s);
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(w.Written.ToArray()));
    }

    [ContentFact]
    public void ACaptainFollowsTheJoinNameUntilRenamed()
    {
        var strings = TestContent.With(GameStrings.Load);
        var world = NewWorld();
        world.Join("g", "Guest");
        world.Leave("g");
        world.Join("g", "Guest Renamed On Purpose");
        var g = world.Find("g")!;
        Assert.Equal("Guest Renamed O", ShipNames.Captain(g, strings));
        Assert.Equal("Wayfarer", ShipNames.Ship(g, strings));
        Assert.Equal("Zelnick", ShipNames.Captain(world.Host, strings));
        Assert.Equal("Vindicator", ShipNames.Ship(world.Host, strings));
        var loaded = HostSave.FromJson(HostSave.ToJson(world));
        Assert.Equal("Guest Renamed O", ShipNames.Captain(loaded.Find("g")!, strings));
        // A captain who was renamed keeps it whatever name they join with.
        world.Rename("g", null, "Ripley");
        world.Leave("g");
        world.Join("g", "Someone Else");
        Assert.Equal("Ripley", ShipNames.Captain(world.Find("g")!, strings));
        Assert.Equal("Ripley", ShipNames.Captain(world.Snapshot().Ships.Single(s => s.Id == "g"), strings));
    }

    [Fact]
    public void NamesAreSavedAndAnOlderSavesEscortIsNamedFromTheSettings()
    {
        var world = NewWorld();
        world.Join("g", "Guest");
        world.Rename("host-id", "Rocinante", "Holden");
        var loaded = HostSave.FromJson(HostSave.ToJson(world));
        Assert.Equal("Rocinante", loaded.Host.Ship.Name);
        Assert.Equal("Holden", loaded.Host.CaptainName);
        Assert.Equal("Wayfarer", loaded.Find("g")!.Ship.Name);
        Assert.Null(loaded.Find("g")!.CaptainName);

        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["version"] = 6;
        foreach (var slots in node["players"]!.AsArray())
            slots!.AsObject().Remove("modules");   // saves before 9 have no slots
        foreach (var p in node["players"]!.AsArray())
        {
            p!.AsObject().Remove("ship_name");
            p.AsObject().Remove("captain_name");
        }
        node["settings"]!["escort_ship_name"] = "Old Faithful";
        var old = HostSave.FromJson(node.ToJsonString());
        Assert.Null(old.Host.Ship.Name);
        Assert.Equal("Old Faithful", old.Find("g")!.Ship.Name);

        node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["players"]![0]!["captain_name"] = "Much too long a captain";
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString()));
        node["players"]![0]!["captain_name"] = "two\nlines";
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString()));
        // An older save can't have names.
        node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["version"] = 6;
        foreach (var slots in node["players"]!.AsArray())
            slots!.AsObject().Remove("modules");   // saves before 9 have no slots
        Assert.Throws<InvalidDataException>(() => HostSave.FromJson(node.ToJsonString()));
    }

    [Fact]
    public void AMissingEscortNameSettingIsTheDefault()
    {
        var world = NewWorld();
        world.Join("g", "Guest");
        // An older save with no name for its escort, and the setting null.
        var node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["version"] = 6;
        foreach (var slots in node["players"]!.AsArray())
            slots!.AsObject().Remove("modules");   // saves before 9 have no slots
        foreach (var p in node["players"]!.AsArray())
        {
            p!.AsObject().Remove("ship_name");
            p.AsObject().Remove("captain_name");
        }
        node["settings"]!["escort_ship_name"] = null;
        var old = HostSave.FromJson(node.ToJsonString());
        Assert.Equal("Wayfarer", old.Find("g")!.Ship.Name);
        // A newer one with the setting null: a new guest is named from the default.
        node = JsonNode.Parse(HostSave.ToJson(world))!;
        node["settings"]!["escort_ship_name"] = null;
        var loaded = HostSave.FromJson(node.ToJsonString());
        loaded.Join("h", "Another");
        Assert.Equal("Wayfarer", loaded.Find("h")!.Ship.Name);
    }
}
