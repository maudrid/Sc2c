// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Data;
using Crateris.Core.Hyperspace;
using Crateris.Core.World;

namespace Crateris.Core.Tests;

public class GameWorldTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "data");

    private static GameWorld NewWorld(GameSettings? settings = null) =>
        GameWorld.NewGame(settings ?? new GameSettings(), NewGameData.Load(DataDir), "host-id", "Host");

    [Fact]
    public void New_game_uses_uqm_starting_values()
    {
        var data = NewGameData.Load(DataDir);
        Assert.Equal((2155, 2, 17), (data.Year, data.Month, data.Day));
        Assert.Equal(new ShipLoadout(2, 2, 1, 0, 50, 1000), data.Flagship);

        var world = NewWorld();
        var f = world.Host.Ship.Flight;
        Assert.Equal((1752, 1450), ((int)f.UniverseX, (int)f.UniverseY));
        Assert.Equal(ShipDrive.ForHyperspace(2, 2), f.Drive);
        Assert.Equal(6000u, f.FuelCapacity);
        Assert.True(world.Host.Present);
    }

    [Fact]
    public void First_join_starts_an_escort_at_the_host_with_undocking()
    {
        var world = NewWorld();
        for (int i = 0; i < 100; i++)
        {
            world.SubmitInput("host-id", world.Frame + 1, ShipInput.Thrust);
            world.Step();
        }
        var host = world.Host.Ship.Flight;

        Assert.Equal(JoinResult.Joined, world.Join("guest-1", "Guest"));
        var guest = world.Find("guest-1")!;
        Assert.Equal(ShipKind.Escort, guest.Ship.Kind);
        Assert.Equal((host.LogX, host.LogY, host.Facing), (guest.Ship.Flight.LogX, guest.Ship.Flight.LogY, guest.Ship.Flight.Facing));
        Assert.True(guest.UndockPending);
        Assert.True(guest.Ship.Flight.Velocity.IsZero);

        world.UndockShown("guest-1");
        Assert.False(guest.UndockPending);
    }

    [Fact]
    public void Leaving_keeps_the_ship_and_rejoining_returns_to_it()
    {
        var world = NewWorld();
        world.Join("guest-1", "Guest");
        for (int i = 0; i < 60; i++)
        {
            world.SubmitInput("guest-1", world.Frame + 1, ShipInput.Thrust);
            world.Step();
        }
        var f = world.Find("guest-1")!.Ship.Flight;
        var (x, y, fuel) = (f.LogX, f.LogY, f.Fuel);

        world.Leave("guest-1");
        Assert.DoesNotContain(world.Snapshot().Ships, s => s.Id == "guest-1");
        for (int i = 0; i < 50; i++)
            world.Step();
        Assert.Equal((x, y, fuel), (f.LogX, f.LogY, f.Fuel)); // not simulated while away

        Assert.Equal(JoinResult.Rejoined, world.Join("guest-1", "Guest again"));
        var back = world.Snapshot().Ships.Single(s => s.Id == "guest-1");
        Assert.Equal("Guest again", back.Name);
        Assert.Equal((0, 0, false), (back.VelocityX, back.VelocityY, back.Thrusting)); // waited, stopped
        Assert.True(back.UndockPending); // never shown, so still owed
        Assert.Equal(JoinResult.AlreadyPresent, world.Join("guest-1", "x"));
    }

    [Fact]
    public void At_most_four_players_at_once()
    {
        var world = NewWorld();
        Assert.Equal(JoinResult.Joined, world.Join("a", "A"));
        Assert.Equal(JoinResult.Joined, world.Join("b", "B"));
        Assert.Equal(JoinResult.Joined, world.Join("c", "C"));
        Assert.Equal(JoinResult.Full, world.Join("d", "D"));
        world.Leave("a");
        Assert.Equal(JoinResult.Joined, world.Join("d", "D"));
        Assert.Equal(5, world.Players.Count);
    }

    [Fact]
    public void Players_fly_independently_in_the_same_hyperspace()
    {
        var world = NewWorld();
        world.Join("guest-1", "Guest");
        for (int i = 0; i < 80; i++)
        {
            world.SubmitInput("host-id", world.Frame + 1, ShipInput.Thrust);
            world.SubmitInput("guest-1", world.Frame + 1, i < 8 ? ShipInput.Right : ShipInput.Thrust);
            world.Step();
        }
        var snap = world.Snapshot();
        var host = snap.Ships.Single(s => s.IsHost);
        var guest = snap.Ships.Single(s => !s.IsHost);
        Assert.Equal(0, host.Facing);
        Assert.NotEqual(0, guest.Facing);
        Assert.True(host.LogY < world.Find("guest-1")!.Ship.Flight.LogY + 1);
        Assert.NotEqual((host.LogX, host.LogY), (guest.LogX, guest.LogY));
    }

    [Fact]
    public void Input_is_applied_on_its_frame_and_repeats_until_changed()
    {
        var buffer = new InputBuffer();
        buffer.Submit(5, ShipInput.Thrust);
        buffer.Submit(8, ShipInput.Left);
        Assert.Equal(ShipInput.None, buffer.Take(4));
        Assert.Equal(ShipInput.Thrust, buffer.Take(5));
        Assert.Equal(ShipInput.Thrust, buffer.Take(7));
        Assert.Equal(ShipInput.Left, buffer.Take(8));
        buffer.Submit(3, ShipInput.Right); // late: applied on the next frame
        Assert.Equal(ShipInput.Right, buffer.Take(9));
    }

    [Fact]
    public void Clock_runs_at_the_constant_rate()
    {
        var world = NewWorld(new GameSettings { SecondsPerGameDay = 1 });
        for (int i = 0; i < 24 * 12; i++)
            world.Step();
        Assert.Equal((2155, 3, 1), (world.Clock.Year, world.Clock.Month, world.Clock.Day)); // 2155 isn't a leap year

        world.Clock.Paused = true;
        for (int i = 0; i < 24 * 5; i++)
            world.Step();
        Assert.Equal(1, world.Clock.Day);
    }

    [Fact]
    public void Calendar_matches_uqm_leap_years()
    {
        Assert.True(GameClock.IsLeapYear(2156));
        Assert.False(GameClock.IsLeapYear(2100));
        Assert.True(GameClock.IsLeapYear(2400));
        var c = new GameClock(2156, 2, 28);
        c.NextDay();
        Assert.Equal((2, 29), (c.Month, c.Day));
        c = new GameClock(2155, 12, 31);
        c.NextDay();
        Assert.Equal((2156, 1, 1), (c.Year, c.Month, c.Day));
    }

    [Fact]
    public void Host_save_round_trips_every_player()
    {
        var world = NewWorld();
        world.Join("guest-1", "Guest");
        world.Join("guest-2", "Away");
        for (int i = 0; i < 77; i++)
        {
            world.SubmitInput("host-id", world.Frame + 1, i % 3 == 0 ? ShipInput.Thrust | ShipInput.Left : ShipInput.Thrust);
            world.SubmitInput("guest-1", world.Frame + 1, ShipInput.Thrust);
            world.Step();
        }
        world.Leave("guest-2");

        string json = HostSave.ToJson(world);
        var loaded = HostSave.FromJson(json);
        foreach (var p in world.Players)
            world.Leave(p.Id);
        Assert.Equal(HostSave.ToJson(world), HostSave.ToJson(loaded));
        Assert.All(loaded.Players, p => Assert.False(p.Present));
        Assert.Equal(world.Frame, loaded.Frame);

        // A loaded game continues exactly like the original once everyone
        // is back (the original's players have left and rejoin too).
        foreach (var w in new[] { world, loaded })
        {
            w.Join("host-id", "Host");
            w.Join("guest-1", "Guest");
            for (int i = 0; i < 40; i++)
            {
                w.SubmitInput("host-id", w.Frame + 1, ShipInput.Thrust);
                w.Step();
            }
        }
        var a = world.Snapshot();
        var b = loaded.Snapshot();
        Assert.Equal((a.Frame, a.Year, a.Month, a.Day), (b.Frame, b.Year, b.Month, b.Day));
        Assert.Equal(a.Ships, b.Ships);
    }
}

public class ProtocolTests
{
    [Fact]
    public void Snapshot_and_welcome_round_trip()
    {
        var world = GameWorld.NewGame(new GameSettings(),
            NewGameData.Load(Path.Combine(AppContext.BaseDirectory, "data")), "h", "Host");
        world.Join("g", "Guest");
        world.SubmitInput("g", 1, ShipInput.Thrust);
        world.Step();
        var snap = world.Snapshot();
        var back = Crateris.Core.Net.Protocol.Decode<WorldSnapshot>(Crateris.Core.Net.Protocol.Encode(snap));
        Assert.Equal(snap.Frame, back.Frame);
        Assert.Equal(snap.Ships, back.Ships);

        var w = new Crateris.Core.Net.Protocol.Welcome(JoinResult.Rejoined, "g", 2, "hi");
        Assert.Equal(w, Crateris.Core.Net.Protocol.Decode<Crateris.Core.Net.Protocol.Welcome>(
            Crateris.Core.Net.Protocol.Encode(w)));
    }
}
