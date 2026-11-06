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
        // A delta carries a rename, and back to the default.
        world.Rename("g", null, "Captain G");
        world.Step();
        var next = world.Snapshot();
        SnapshotCodec.Encode(w, next, before);
        var delta = SnapshotCodec.Decode(w.Written.ToArray(), got);
        Assert.Equal("Captain G", delta.Ships.Single(s => s.Id == "g").CaptainName);
        Assert.Equal("Starlight Expre", delta.Ships.Single(s => s.Id == "g").ShipName);
    }

    [Fact]
    public void ANameLongerThanUqmsIsRefusedOnTheWire()
    {
        var world = NewWorld();
        var snap = world.Snapshot();
        var s = snap with { Ships = snap.Ships.Select(sh => sh with { ShipName = "sixteen bytes!!!" }).ToArray() };
        var w = new WireWriter();
        SnapshotCodec.Encode(w, s);
        Assert.Throws<InvalidDataException>(() => SnapshotCodec.Decode(w.Written.ToArray()));
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
    }
}
