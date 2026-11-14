// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Ported from The Ur-Quan Masters 0.8.0
// (src/uqm/ships/syreen/syreen.c: init_syreen, initialize_dagger,
// spawn_crew, syreen_intelligence, syreen_postprocess), Copyright Paul
// Reiche, Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM
// contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

namespace Crateris.Core.Combat;

/// <summary>
/// syreen.c: the Syreen Penetrator. Its weapon is a dagger ahead; its
/// special, the Syreen song, calls crew off every opposing ship within
/// reach (all but one of each's crew, more the nearer it is), who float
/// towards the Syreen (<see cref="Battle.AbandonShip"/>) and are picked
/// up by whichever ship touches them. With several players, every
/// player ship in reach loses crew to an enemy Syreen (stage 4b decision
/// 6); a player's Syreen leaves its teammates be (they're its side's).
/// <para>
/// Its AI sings at an enemy ship within 14 frames with more than one crew
/// that isn't crew-immune.
/// </para>
/// </summary>
public sealed class SyreenShip : IShipCode
{
    public const string Id = "syreen";

    private readonly ShipDescription _description;
    private readonly Views? _hull, _dagger;

    /// <param name="description">The Syreen's description (data/ships.json's syreen).</param>
    /// <param name="hull">Its ship sheets (ship_data.ship).</param>
    /// <param name="dagger">Its dagger's sheets (ship_data.weapon).</param>
    public SyreenShip(ShipDescription description, Views? hull, Views? dagger)
    {
        _description = RaceShips.Check(description, Id);
        _hull = hull;
        _dagger = dagger;
    }

    private sealed record Song(int SpecialEnergyCost, int AbandonerRange, int MaxAbandoners);

    /// <summary>init_syreen: syreen_desc with its sheets and syreen.c's battle code.</summary>
    public RaceDesc Load(Battle battle, Starship starship)
    {
        var d = _description;
        var rd = RaceShips.NewRaceDesc(d, _hull, _dagger, null);
        var dagger = new MissileBlock
        {
            Flags = ElementFlags.IgnoreSimilar, PixOffs = (short)d.Define("SYREEN_OFFSET"),
            Speed = (short)d.Define("MISSILE_SPEED"), HitPoints = (short)d.Define("MISSILE_HITS"),
            Damage = (short)d.Define("MISSILE_DAMAGE"), Life = (ushort)d.Define("MISSILE_LIFE"),
            BlastOffs = (short)d.Define("MISSILE_OFFSET"),
        };
        var song = new Song(d.Define("SPECIAL_ENERGY_COST"), d.Define("ABANDONER_RANGE"), d.Define("MAX_ABANDONERS"));
        rd.InitWeapon = e => InitializeDagger(battle, e, dagger);
        rd.Postprocess = e => Postprocess(battle, e, song);
        rd.Intelligence = (ship, concerns, count) => Intelligence(battle, ship, concerns, count);
        return rd;
    }

    // initialize_dagger: a dagger ahead.
    private static IReadOnlyList<Element?> InitializeDagger(Battle battle, Element ship, MissileBlock block)
    {
        var starShip = ship.Parent!;
        block.Cx = ship.Next.X;
        block.Cy = ship.Next.Y;
        block.FrameArray = starShip.RaceDesc!.Weapon!;
        block.Face = block.Index = starShip.ShipFacing;
        block.Sender = ship.PlayerNr;
        return new[] { battle.InitializeMissile(block) };
    }

    // spawn_crew: on the ship, a helper element that dies this frame; in
    // its death, after every element has moved, each ship of another side
    // with more than one crew within reach loses crew (more the nearer,
    // leaving it one), floating off towards the Syreen.
    private static void SpawnCrew(Battle battle, Element e, Song song)
    {
        if ((e.StateFlags & ElementFlags.PlayerShip) != 0)
        {
            var crew = battle.List.Alloc();
            if (crew != null)
            {
                crew.Next.X = e.Next.X;
                crew.Next.Y = e.Next.Y;
                crew.PlayerNr = e.PlayerNr;
                crew.StateFlags = ElementFlags.Appearing | ElementFlags.NonSolid | ElementFlags.FiniteLife;
                crew.LifeSpan = 0;
                crew.DeathFunc = c => SpawnCrew(battle, c, song);
                crew.Parent = e.Parent;
                crew.Target = null;
                battle.List.Put(crew);
            }
            return;
        }

        int range = song.AbandonerRange;
        for (Element? o = battle.List.Head, next; o != null; o = next)
        {
            next = o.Successor;
            if ((o.StateFlags & ElementFlags.PlayerShip) == 0 || o.PlayerNr == e.PlayerNr || o.CrewLevel <= 1)
                continue;
            // Across the seam where contact is tested so (decision 5).
            int dx = battle.SeamDeltaX(o.Next.X - e.Next.X), dy = battle.SeamDeltaY(o.Next.Y - e.Next.Y);
            if (dx < 0)
                dx = -dx;
            if (dy < 0)
                dy = -dy;
            dx = Battle.WorldToDisplay((short)dx);
            dy = Battle.WorldToDisplay((short)dy);
            uint dSquared;
            if (dx <= range && dy <= range
                && (dSquared = (uint)((ushort)dx * (ushort)dx) + (uint)((ushort)dy * (ushort)dy))
                <= (uint)((ushort)range * (ushort)range))
            {
                int crewLoss = song.MaxAbandoners * (range - Uqm.UqmMath.SquareRoot(dSquared)) / range + 1;
                if (crewLoss >= o.CrewLevel)
                    crewLoss = o.CrewLevel - 1;
                battle.AbandonShip(o, e, (ushort)crewLoss);
            }
        }
    }

    // syreen_intelligence: the song at an enemy ship within 14 frames with
    // crew to lose and not crew-immune, once ready.
    private static void Intelligence(Battle battle, Element ship, EvaluateDesc[] concerns, int concernCounter)
    {
        battle.ShipIntelligence(ship, concerns, concernCounter);

        ref var enemy = ref concerns[Battle.EnemyShipIndex];
        if (enemy.Object != null)
        {
            var starShip = ship.Parent!;
            var enemyStarShip = enemy.Object.Parent!;
            if ((enemyStarShip.RaceDesc!.ShipFlags & ShipFlags.CrewImmune) == 0
                && starShip.SpecialCounter == 0
                && enemy.Object.CrewLevel > 1
                && enemy.WhichTurn <= 14)
                starShip.ShipInputState |= StatusFlags.Special;
            else
                starShip.ShipInputState &= ~StatusFlags.Special;
        }
    }

    // syreen_postprocess: the song when pressed, ready and paid for (its
    // SYREEN_SONG sound).
    private static void Postprocess(Battle battle, Element e, Song song)
    {
        var starShip = e.Parent!;
        if ((starShip.CurStatusFlags & StatusFlags.Special) != 0
            && starShip.SpecialCounter == 0
            && battle.DeltaEnergy(e, (short)-song.SpecialEnergyCost))
        {
            SpawnCrew(battle, e, song);
            starShip.SpecialCounter = starShip.RaceDesc!.Characteristics.SpecialWait;
        }
    }
}
