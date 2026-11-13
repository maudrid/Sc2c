// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Starbase;
using Crateris.Core.Systems;
using Crateris.Core.World;

namespace Crateris.Core.Session;

/// <summary>The rescue screen as drawn: the wreck (its ship's and captain's names), the crew to give, and the most there is.</summary>
public sealed record RescueShown(int Row, string? Wreck, string WreckName, string CaptainName, int WrecksHere, int Crew, int Most,
    int Have, int Room);

/// <summary>
/// The rescue screen (stage 4a item 12, decision 8): opened as the player
/// flies into a wreck (<see cref="PlayerSession.RescueOffer"/>), it chooses
/// the wreck among those in the same place (the one offered first; the
/// host checks it's in reach, <see cref="GameWorld.RescuesFor"/>) and the
/// crew to give, from 1 to the rescuer's less 1, up to what the wreck
/// holds (its crew capacity: its crew pods or kit; a wreck has no crew).
/// Enter gives it (<see cref="PlayerSession.Revive"/>), Esc closes.
/// </summary>
public sealed class RescueScreen
{
    /// <summary>The rows: the wreck, the crew.</summary>
    public const int WreckRow = 0, CrewRow = 1, Rows = 2;

    public RescueScreen(string wreck) => Wreck = wreck;

    public int Row { get; private set; } = CrewRow;

    /// <summary>The wreck to give crew to (its player's id).</summary>
    public string Wreck { get; private set; }

    /// <summary>The crew to give.</summary>
    public int Crew { get; private set; } = 1;

    /// <summary>The wrecks in the same place as the player's ship (the same part of the same system, or both in hyperspace), the first found first.</summary>
    public static List<ShipSnapshot> WrecksHere(WorldSnapshot? snapshot, string me)
    {
        if (snapshot?.Ships.FirstOrDefault(s => s.Id == me) is not { } mine)
            return new List<ShipSnapshot>();
        bool hyper = mine.Activity == Activity.Hyperspace || mine.SystemStar is null;
        return snapshot.Ships.Where(s => s.Id != me && s.Wrecked
                && (hyper ? s.Activity == Activity.Hyperspace || s.SystemStar is null
                    : s.SystemStar == mine.SystemStar && s.InnerPlanet == mine.InnerPlanet))
            .ToList();
    }

    /// <summary>A ship's crew capacity: its crew pods or kit, from its modules.</summary>
    public static int Room(ShipSnapshot s) => s.Modules is { } m ? ShipModules.Restore(m).CrewCapacity : 0;

    /// <summary>The screen as it stands for <paramref name="me"/>, the crew kept within what may be given.</summary>
    public RescueShown Follow(WorldSnapshot? snapshot, string me)
    {
        var wrecks = WrecksHere(snapshot, me);
        var wreck = wrecks.FirstOrDefault(w => w.Id == Wreck);
        int have = snapshot?.Ships.FirstOrDefault(s => s.Id == me)?.Hold?.Crew ?? 0;
        int room = wreck is null ? 0 : Room(wreck);
        int most = Math.Max(0, Math.Min(have - 1, room));
        Crew = Math.Clamp(Crew, Math.Min(1, most), Math.Max(1, most));
        return new RescueShown(Row, wreck?.Id, wreck?.ShipName ?? wreck?.Name ?? "", wreck?.CaptainName ?? wreck?.Name ?? "",
            wrecks.Count, Crew, most, have, room);
    }

    /// <summary>A key; returns the revival to send if Enter gave one.</summary>
    public (string Wreck, int Crew)? Press(MenuKey key, WorldSnapshot? snapshot, string me)
    {
        var shown = Follow(snapshot, me);
        switch (key)
        {
            case MenuKey.Up:
            case MenuKey.Down:
                Row = (Row + 1) % Rows;
                break;
            case MenuKey.Left or MenuKey.Right when Row == WreckRow:
                var wrecks = WrecksHere(snapshot, me);
                if (wrecks.Count > 0)
                {
                    int i = wrecks.FindIndex(w => w.Id == Wreck);
                    int d = key == MenuKey.Left ? -1 : 1;
                    Wreck = wrecks[((i < 0 ? 0 : i + d) % wrecks.Count + wrecks.Count) % wrecks.Count].Id;
                }
                break;
            case MenuKey.Left or MenuKey.Right or MenuKey.PageUp or MenuKey.PageDown:
                int step = key is MenuKey.PageUp or MenuKey.PageDown ? 10 : 1;
                int sign = key is MenuKey.Left or MenuKey.PageDown ? -1 : 1;
                Crew = Math.Clamp(Crew + sign * step, 1, Math.Max(1, shown.Most));
                break;
            case MenuKey.Select when shown.Wreck is not null:
                // Even past what may be given (none to spare, no room): the host says why, as it refuses it.
                return (shown.Wreck, Crew);
        }
        return null;
    }
}
