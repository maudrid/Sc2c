// Attack on Crateris
// Copyright (C) 2026 Attack on Crateris contributors.
// Follows The Ur-Quan Masters 0.8.0 (src/uqm/planets/generate/gen*.c: each
// generator's generateOrbital; src/uqm/comm.c: InitCommunication;
// src/uqm/encount.c: BuildBattle, UninitEncounter), Copyright Paul Reiche,
// Fred Ford and Toys for Bob, Inc. 1992-2002, and the UQM contributors.
//
// This program is free software; you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation; either version 2 of the License, or
// (at your option) any later version.

using Crateris.Core.Comm;
using Crateris.Core.Story;
using Crateris.Core.Systems;

namespace Crateris.Core.World;

/// <summary>
/// A world whose orbit starts a race's talk instead (a generator's
/// generateOrbital calling InitCommunication: stage 3b-1 item 7, the
/// stage 3b-2 plan's item 1): the special system, the planet and the moon
/// (matchWorld's), the race whose ships are there and whose lock the talk
/// takes, and GLOBAL_FLAGS_AND_DATA as the generator sets it for the talk
/// (1 &lt;&lt; 7 at a race's own world, 1 &lt;&lt; 6 at its second one,
/// 0 where it sets none). <see cref="Generator"/> names the C function.
/// </summary>
internal sealed record TalkWorld(string Special, int Planet, int? Moon, string Race, int GlobalFlags, string Generator)
{
    /// <summary>CloneShipFragment's INFINITE_FLEET: <see cref="Fleet"/>'s answer for an infinite fleet.</summary>
    public const int InfiniteFleet = -1;

    /// <summary>
    /// The conversation the talk starts (InitCommunication's), where it isn't
    /// the race's own met in space (races.h's RACE_COMMUNICATION): the
    /// Slylandro at home's SLYLANDRO_HOME_CONVERSATION, the Syreen's (who
    /// have none in space). Null: the race's own.
    /// </summary>
    public string? Conversation { get; init; }

    /// <summary>
    /// Whether the world is this entry's at all now: the generator's first
    /// test beyond matchWorld (the Taalo protector not yet taken, the bomb
    /// not yet taken, the Chmmr not yet unleashed). Null: always.
    /// </summary>
    public Func<StoryState, bool>? Matches { get; init; }

    /// <summary>
    /// The generator's test that starts the talk, in the generator's own
    /// order: the story's conditions and StartSphereTracking (the race
    /// lives), given as the second argument, so its tracking happens where
    /// UQM's does. By default the race living.
    /// </summary>
    public Func<StoryState, Func<bool>, bool> Guarded { get; init; } = (_, lives) => lives();

    /// <summary>
    /// The ships there (npc_built_ship_q as the generator clones them):
    /// <see cref="InfiniteFleet"/>, a guard of so many ships each with its
    /// race's starting crew (CloneShipFragment with crew_level 0), or 0 for
    /// none (the Slylandro at home, the Syreen's starbase: no ship cloned).
    /// </summary>
    public Func<StoryState, int> Fleet { get; init; } = _ => InfiniteFleet;

    /// <summary>Whether the race's groups in the system stand down for the talk (NotifyOthers IPNL_ALL_CLEAR).</summary>
    public bool StandDown { get; init; } = true;

    /// <summary>Whether the system's groups are put aside for the talk (PutGroupInfo GROUP_SAVE_IP, GetGroupInfo GROUP_LOAD_IP after).</summary>
    public bool PutAside { get; init; } = true;

    /// <summary>
    /// After the talk (and its battle), whether the ship stays out of orbit
    /// (generateOrbital returning at once), given whether any of the ships
    /// are left (GetHeadLink (&amp;npc_built_ship_q): an infinite fleet's
    /// always). Null: always (the generator returns whatever happened).
    /// Else the generator falls through to the orbit, setting
    /// <see cref="Unguarded"/>.
    /// </summary>
    public Func<StoryState, bool, bool>? Stays { get; init; }

    /// <summary>The story flag the generator sets as it falls through to the orbit (TAALO_UNPROTECTED, SUN_DEVICE_UNGUARDED, BOMB_UNPROTECTED), or null.</summary>
    public string? Unguarded { get; init; }

    /// <summary>
    /// Whether <see cref="Unguarded"/> is set too when the world is orbited
    /// with no talk while it's this entry's (genorz.c sets TAALO_UNPROTECTED
    /// after its whole test, the Orz extinct included).
    /// </summary>
    public bool UnguardedWhenOrbited { get; init; }
}

/// <summary>The worlds whose orbit starts a race's talk (stage 3b-1 item 7; generalised by the stage 3b-2 plan's item 1).</summary>
public sealed partial class GameWorld
{
    private static int Flag(StoryState s, string field) => s.Get(field);

    /// <summary>
    /// Every world whose orbit starts a talk in stage 3b-2's races, by its
    /// generator's generateOrbital, in the generator's order where two
    /// share a world (the bomb's moon: the Utwig, then the Druuge). An
    /// entry starts nothing while its talk's script isn't ported
    /// (<see cref="WorldScript"/>): the world is orbited as before; every
    /// entry's is since stage 3b-2 item 11 (the Syreen's starbase the
    /// last), so that's only without the content. Left
    /// out: the Chmmr's own talk at Procyon once they're unleashed and
    /// the bomb ship (genchmmr.c:96, 3c's: the plan's question 8); the
    /// Talking Pet's half of genpet.c (the zombies, the Pet's talks, the
    /// jump to the Ur-Quan: 3c, the plan's left-out table); the Kohr-Ah's
    /// ship at the Zoq-Fot-Pik's world in distress (genzoq.c:96, 3c: with
    /// ZOQFOT_DISTRESS set the world is orbited until then); the Sa-Matra
    /// (gensam.c, the endgame).
    /// </summary>
    internal static readonly IReadOnlyList<TalkWorld> TalkWorlds = new TalkWorld[]
    {
        // genilw.c:77.
        new("ILWRATH_DEFINED", 0, null, "ILWRATH_SHIP", 1 << 7, "GenerateIlwrath_generateOrbital"),
        // genspa.c:116: Spathiwa's moon, while the Spathi haven't shielded themselves (spahome.c's talk).
        new("SPATHI_DEFINED", 0, 0, "SPATHI_SHIP", 1 << 7, "GenerateSpathi_generateOrbital")
        {
            Guarded = (s, lives) => Flag(s, "SPATHI_SHIELDED_SELVES") == 0 && lives(),
        },
        // genvux.c:122: the VUX homeworld, and Admiral ZEX at the Maidens' world while he lives.
        new("VUX_DEFINED", 0, null, "VUX_SHIP", 1 << 7, "GenerateVux_generateOrbital"),
        new("MAIDENS_DEFINED", 0, null, "VUX_SHIP", 1 << 6, "GenerateVux_generateOrbital")
        {
            Guarded = (s, lives) => Flag(s, "ZEX_IS_DEAD") == 0 && lives(),
            Stays = (s, _) => Flag(s, "ZEX_IS_DEAD") == 0,
        },
        // genyeh.c:76.
        new("YEHAT_DEFINED", 0, null, "YEHAT_SHIP", 1 << 7, "GenerateYehat_generateOrbital"),
        // genorz.c:84: the Orz homeworld, and the Taalo protector's moon guarded by 14 Orz until none is left;
        // TAALO_UNPROTECTED set whenever either world is orbited past the test (the Orz extinct too).
        new("ORZ_DEFINED", 0, null, "ORZ_SHIP", 1 << 7, "GenerateOrz_generateOrbital")
        {
            Stays = (_, survivors) => survivors,
            Unguarded = "TAALO_UNPROTECTED",
            UnguardedWhenOrbited = true,
        },
        new("TAALO_PROTECTOR_DEFINED", 1, 2, "ORZ_SHIP", 1 << 6, "GenerateOrz_generateOrbital")
        {
            Matches = s => Flag(s, "TAALO_PROTECTOR") == 0,
            Guarded = (s, lives) => Flag(s, "TAALO_UNPROTECTED") == 0 && lives(),
            Fleet = _ => 14,
            Stays = (s, survivors) => survivors && Flag(s, "TAALO_UNPROTECTED") == 0,
            Unguarded = "TAALO_UNPROTECTED",
            UnguardedWhenOrbited = true,
        },
        // genmyc.c:86: the Mycon homeworld, and the Sun Device's world: an infinite fleet, or 5 ships once
        // the Mycon fell for the ambush, until none is left (SUN_DEVICE_UNGUARDED).
        new("MYCON_DEFINED", 0, null, "MYCON_SHIP", 1 << 7, "GenerateMycon_generateOrbital")
        {
            Stays = (_, survivors) => survivors,
            Unguarded = "SUN_DEVICE_UNGUARDED",
        },
        new("SUN_DEVICE_DEFINED", 0, null, "MYCON_SHIP", 1 << 6, "GenerateMycon_generateOrbital")
        {
            Guarded = (s, lives) => lives() && Flag(s, "SUN_DEVICE_UNGUARDED") == 0,
            Fleet = s => Flag(s, "MYCON_FELL_FOR_AMBUSH") == 0 ? TalkWorld.InfiniteFleet : 5,
            Stays = (_, survivors) => survivors,
            Unguarded = "SUN_DEVICE_UNGUARDED",
        },
        // genpku.c:78.
        new("PKUNK_DEFINED", 0, null, "PKUNK_SHIP", 1 << 7, "GeneratePkunk_generateOrbital"),
        // gendru.c:87.
        new("DRUUGE_DEFINED", 0, null, "DRUUGE_SHIP", 1 << 7, "GenerateDruuge_generateOrbital"),
        // genutw.c:102: the Utwig homeworld; the bomb's moon guarded by the Utwig until they have their
        // Ultron back, then by 5 Druuge until none is left (BOMB_UNPROTECTED), the Druuge not standing down.
        new("UTWIG_DEFINED", 0, null, "UTWIG_SHIP", 1 << 7, "GenerateUtwig_generateOrbital"),
        new("BOMB_DEFINED", 5, 1, "UTWIG_SHIP", 1 << 6, "GenerateUtwig_generateOrbital")
        {
            Matches = s => Flag(s, "UTWIG_BOMB") == 0,
            Guarded = (s, lives) => Flag(s, "UTWIG_HAVE_ULTRON") == 0 && lives(),
        },
        new("BOMB_DEFINED", 5, 1, "DRUUGE_SHIP", 1 << 6, "GenerateUtwig_generateOrbital")
        {
            Matches = s => Flag(s, "UTWIG_BOMB") == 0,
            Guarded = (s, lives) => Flag(s, "BOMB_UNPROTECTED") == 0 && lives(),
            Fleet = _ => 5,
            StandDown = false,
            Stays = (_, survivors) => survivors,
            Unguarded = "BOMB_UNPROTECTED",
        },
        // gensup.c:78.
        new("SUPOX_DEFINED", 0, null, "SUPOX_SHIP", 1 << 7, "GenerateSupox_generateOrbital"),
        // genthrad.c:94: the Thraddash homeworld, and the Aqua Helix's world while it's guarded and their
        // mission (THRADD_MISSION 1 to 3) hasn't taken them away.
        new("THRADD_DEFINED", 0, null, "THRADDASH_SHIP", 1 << 7, "GenerateThraddash_generateOrbital"),
        new("AQUA_HELIX_DEFINED", 0, null, "THRADDASH_SHIP", 1 << 6, "GenerateThraddash_generateOrbital")
        {
            Guarded = (s, lives) => lives() && HelixGuarded(s),
            Stays = (s, _) => HelixGuarded(s),
        },
        // genzoq.c:86 (the Kohr-Ah's ship first while ZOQFOT_DISTRESS is set: 3c's, so the world waits for it).
        new("ZOQFOT_DEFINED", 0, null, "ZOQFOTPIK_SHIP", 1 << 7, "GenerateZoqFotPik_generateOrbital")
        {
            Guarded = (s, lives) => lives() && Flag(s, "ZOQFOT_DISTRESS") == 0,
            StandDown = false,
        },
        // genpet.c:81, the Umgah's half: their world while they aren't the Talking Pet's zombies. In the states
        // the Pet's half handles (the zombies, the Umgah extinct: 3c's) the world starts no talk and, never
        // entered (Surface.EnterOrbit), isn't orbited either: the ship is held by it.
        new("TALKING_PET_DEFINED", 0, null, "UMGAH_SHIP", 1 << 7, "GenerateTalkingPet_generateOrbital")
        {
            Guarded = (s, lives) => Flag(s, "UMGAH_ZOMBIE_BLOBBIES") == 0 && lives(),
        },
        // gensly.c:58: the Slylandro gas giant, with no ships and no test.
        new("SLYLANDRO_DEFINED", 3, null, "SLYLANDRO_SHIP", 0, "GenerateSlylandro_generateOrbital")
        {
            Conversation = "SLYLANDRO_HOME_CONVERSATION",
            Guarded = (_, _) => true,
            Fleet = _ => 0,
            StandDown = false,
            PutAside = false,
        },
        // gensyr.c:77: the Syreen's starbase, with no ships and no test.
        new("SYREEN_DEFINED", 0, 0, "SYREEN_SHIP", 0, "GenerateSyreen_generateOrbital")
        {
            Conversation = "SYREEN_CONVERSATION",
            Guarded = (_, _) => true,
            Fleet = _ => 0,
            StandDown = false,
            PutAside = false,
        },
        // genchmmr.c:92: Procyon's Ilwrath, before the Chmmr are unleashed, with the Sun Device aboard
        // while the Ilwrath aren't deceived (the Ilwrath's bit 6 branch, stage 3b-1 item 7's).
        new("CHMMR_DEFINED", 1, null, "ILWRATH_SHIP", 1 << 6, "GenerateChmmr_generateOrbital")
        {
            Matches = s => Flag(s, "CHMMR_UNLEASHED") == 0,
            Guarded = (s, lives) => Flag(s, "SUN_DEVICE_ON_SHIP") != 0 && Flag(s, "ILWRATH_DECEIVED") == 0 && lives(),
            StandDown = false,
        },
    };

    // genthrad.c:101: !HELIX_UNPROTECTED && (BYTE)(THRADD_MISSION - 1) >= 3.
    private static bool HelixGuarded(StoryState s) =>
        Flag(s, "HELIX_UNPROTECTED") == 0 && (byte)(Flag(s, "THRADD_MISSION") - 1) >= 3;

    /// <summary>The worlds whose orbit starts a talk: <see cref="TalkWorlds"/>; tests give their own.</summary>
    internal IReadOnlyList<TalkWorld> WorldTalkTable { get; set; } = TalkWorlds;

    /// <summary>
    /// The ported script a world's talk runs (init_race: the conversation's,
    /// chosen by the world's GLOBAL_FLAGS_AND_DATA), or null: none without
    /// the content or the locks, or while it isn't ported.
    /// </summary>
    internal string? WorldScript(TalkWorld w)
    {
        if (TalkLibrary is not { } library || Story is not { } story || DialogueLocks is null
            || (w.Conversation ?? ConversationOf(story.Rules.RaceIndex(w.Race))) is not { } conversation)
            return null;
        string script = TalkLibrary.ScriptFor(library.Scripts, conversation, story, w.GlobalFlags);
        return TalkLibrary.Ported.Contains(script) ? script : null;
    }

    // The conversation a world's talk starts.
    private string WorldConversation(TalkWorld w) => w.Conversation ?? ConversationOf(Story!.Rules.RaceIndex(w.Race))!;

    /// <summary>
    /// Orbiting a world, as its generator's generateOrbital decides: the
    /// <see cref="WorldTalkTable"/> entry whose talk starts instead of the
    /// orbit, or null for an orbit. The entries at the world that are its
    /// now (<see cref="TalkWorld.Matches"/>) are tested in order; the first
    /// guarded (<see cref="TalkWorld.Guarded"/>) starts its talk, if its
    /// script is ported (<see cref="WorldScript"/>), else the world is
    /// orbited as before, nothing changed. Otherwise it's an orbit, with
    /// the generator's side effects: the tests' StartSphereTracking, and
    /// TAALO_UNPROTECTED at the Orz's worlds
    /// (<see cref="TalkWorld.UnguardedWhenOrbited"/>), each only for an
    /// entry whose script is ported.
    /// </summary>
    internal TalkWorld? WorldTalkAt(int star, int planet, int? moon)
    {
        if (Systems is null || Story is not { } story)
            return null;
        string? special = Systems.Map.Stars[star].Special;
        var here = WorldTalkTable.Where(w => w.Special == special && w.Planet == planet && w.Moon == moon
                                             && (w.Matches?.Invoke(story) ?? true)).ToList();
        // Which starts its talk, asked without tracking a sphere.
        int first = here.FindIndex(w => w.Guarded(story, () => story.WouldTrackSphere(w.Race)));
        // As the generator does it, the tests and their tracking with them,
        // up to that one (its talk not ported yet: orbited as before).
        foreach (var w in first >= 0 ? here.Take(first + 1) : here)
        {
            if (WorldScript(w) is null)
                continue;
            if (w.Guarded(story, () => story.StartSphereTracking(w.Race)))
                return w;
            if (first < 0 && w.UnguardedWhenOrbited && w.Unguarded is { } flag)
                story.Set(flag, 1);
        }
        return null;
    }

    /// <summary>
    /// The race (races.h's number) whose talk orbiting a world starts now
    /// (<see cref="WorldTalkAt"/>, with its side effects), or -1.
    /// </summary>
    internal int WorldTalkRace(int star, int planet, int? moon) =>
        WorldTalkAt(star, planet, moon) is { } w ? Story!.Rules.RaceIndex(w.Race) : -1;

    // The table's entry a meeting at a world was started by (a saved copy's meeting, question 13), or null.
    private TalkWorld? TalkWorldOf(int? star, (int Planet, int? Moon)? world, int race)
    {
        if (star is not int s || world is not var (planet, moon) || Systems is null || Story is null)
            return null;
        string? special = Systems.Map.Stars[s].Special;
        string raceId = Story.Rules.Races[race];
        return WorldTalkTable.FirstOrDefault(w => w.Special == special && w.Planet == planet && w.Moon == moon && w.Race == raceId);
    }

    /// <summary>
    /// A player's ship orbits a world whose talk starts instead
    /// (<see cref="WorldTalkAt"/>; the generator's generateOrbital): no
    /// orbit, the ship by the world, and the player meets the race's ships
    /// there as the generator clones them (<see cref="TalkWorld.Fleet"/>:
    /// an infinite fleet, a guard, or none). The system's groups are put
    /// aside for the talk, the race's standing down
    /// (<see cref="SystemGroups.StartWorldMeeting"/>), where the generator
    /// does so; the talk is told where it is (its own
    /// GLOBAL_FLAGS_AND_DATA) and, hostile, shows the encounter screen; a
    /// battle is against the ships there. With ships, BuildBattle's
    /// ESCAPE_COUNTER; with none, none (BuildBattle returns at once, or
    /// isn't called: the Syreen's) and no battle can follow. Under the
    /// lock of the talk's conversation, as a group met (decision 5): while
    /// another player holds it, nothing starts and the player is told
    /// who's talking. A player in a meeting (one whose battle others fight
    /// on, handoff question 3), a visit or a battle starts no talk, as such
    /// a player meets no group and starts no starbase visit: the world is
    /// an ordinary fly-by, the ship by it with no orbit (UQM never orbits
    /// it while its talk would start).
    /// </summary>
    private void MeetAtWorld(Player player, int star, (int Planet, int? Moon) world, TalkWorld w)
    {
        if (player.Ship is not { Meeting: null, Visit: null, Battle: null })
            return;
        var rules = Story!.Rules;
        int race = rules.RaceIndex(w.Race);
        string conversation = WorldConversation(w);
        if (TalkHolder(conversation) is { } holder && holder != player.Id)
        {
            TellWhoIsTalking(player, conversation, race);
            return;
        }
        if (!DialogueLocks!.TryTake(conversation, player.Id, out _))
        {
            TellWhoIsTalking(player, conversation, race);
            return;
        }
        if (SystemGroups is { } sg && sg.Systems.TryGetValue(star, out var d) && w.PutAside)
            sg.StartWorldMeeting(d, w.StandDown ? race : -1);
        int fleet = w.Fleet(Story);
        string ship = rules.RaceShips[race].Ship;
        var ships = fleet == TalkWorld.InfiniteFleet ? new[] { new EnemyFleetShip(ship) }
            : Enumerable.Range(0, fleet).Select(_ => new EnemyFleetShip(ship)).ToArray();
        var m = new Meeting(player, race, rules.Races[race], ships, NextRound())
        {
            Id = ++_meetingIds,
            Star = star,
            World = world,
            Infinite = fleet == TalkWorld.InfiniteFleet,
            Conversation = conversation,
        };
        m.WorldTalk = w;
        // BuildBattle (NPC_PLAYER_NUM) in a system: ESCAPE_COUNTER, with ships to build.
        if (ships.Length > 0 && player.Ship.Ip is { Orbit: null } ip)
            ip.EscapeCounter = IpFlight.EscapeFrames;
        player.Ship.Meeting = m;
        _newMeetings.Add(m);
        TellMeetingBegun(m);
        BeginTalk(m);
    }

    /// <summary>
    /// A world's talk (<see cref="MeetAtWorld"/>), with the answerer's ship
    /// and the talk's own fields: GLOBAL_FLAGS_AND_DATA as the generator set
    /// it, which also picks the script where init_race chooses by it (the
    /// Spathi at Spathiwa run spahome.c); null while the script isn't ported.
    /// </summary>
    private Talk? WorldTalk(Player player, TalkWorld w, uint seed) =>
        WorldScript(w) is null ? null : ConversationTalk(player, WorldConversation(w), seed, w.GlobalFlags);

    /// <summary>
    /// After a world's talk and any battle (the generator's code after
    /// InitCommunication): unless the ship stays out of orbit
    /// (<see cref="TalkWorld.Stays"/>, given whether any of the ships are
    /// left), the generator falls through to the orbit: its flag set (a
    /// guard gone: <see cref="TalkWorld.Unguarded"/>) and the player's ship
    /// put in orbit around the world, as UQM's EnterPlanetOrbit goes on to
    /// PlanetOrbitMenu, if it's still by it in flight there, out of any
    /// meeting, visit or battle, and was in the battle to its end (one who
    /// left it earlier is flying elsewhere: the guard's end only sets the flag).
    /// </summary>
    private void AfterWorldTalk(Meeting m, bool survivors)
    {
        if (m.WorldTalk is not { Stays: { } stays } w || Story is not { } story || stays(story, survivors))
            return;
        if (w.Unguarded is { } flag)
            story.Set(flag, 1);
        var player = m.Player;
        var ship = player.Ship;
        if (m.LeftBattleEarly || !player.Present || ship.Destroyed || ship is not { Meeting: null, Visit: null, Battle: null }
            || ship.SystemStar != m.Star || ship.Ip is not { Orbit: null } ip || m.World is not var (planet, moon))
            return;
        ip.PlaceInOrbit(planet, moon);
        if (ip.OrbitData is { Entered: false })
            ip.ReturnFromOrbit();
        else
            InOrbit(ship);
    }

    /// <summary>
    /// UninitEncounter (encount.c:760): Thraddash ships destroyed in a
    /// battle with them (the group's first ship's race, EncounterRace)
    /// while THRADD_MANNER isn't set add to THRADDASH_BODY_COUNT, which
    /// stops at THRADDASH_BODY_THRESHOLD (25): the sum is capped, so a
    /// count set past it comes back to it. <paramref name="killed"/> is the
    /// ships UninitEncounter removes: those whose crew ran out (all, on a
    /// win), every player's kills in the battle among them (the owner's
    /// question 9); an infinite fleet's one ship never is (its crew_level
    /// stays INFINITE_FLEET).
    /// </summary>
    internal static void CountThraddashDead(StoryState story, string race, int killed)
    {
        if (killed == 0 || race != "THRADDASH_SHIP" || story.Get("THRADD_MANNER") != 0)
            return;
        int count = killed + story.Get("THRADDASH_BODY_COUNT");
        if (count > ThraddashBodyThreshold)
            count = ThraddashBodyThreshold;
        story.Set("THRADDASH_BODY_COUNT", count);
    }

    /// <summary>THRADDASH_BODY_THRESHOLD (globdata.h): the body count that wins their alliance.</summary>
    internal const int ThraddashBodyThreshold = 25;
}
