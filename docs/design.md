# AC2C: Co-op Engine Design & Handover

Oct 31, 2026 · @My Dark Lord

> This file is the source of truth. The earlier artifact copy (https://claude.ai/artifact/NjSyab2rXK5BqKwWEKwayh) is no longer kept up to date.

## Overview

AC2C is a free, GPLv2-or-later engine for drop-in co-op play of up to 4 players, built in Godot with C#. It plays The Ur-Quan Masters content, which players download separately.

- **Rewrite, not fork.** UQM is single-player throughout: one global game state, one current activity, 2-sided battles. Its netplay covers only Super Melee. AC2C's server must run a separate activity per player, so the core is new code.
- **Port freely.** Because AC2C is GPL, it can port UQM's data and logic directly, including ship stats, the star map, planet generators, events and conversation logic. The earlier two-agent clean-room process is dropped.
- **Solo to co-op and back.** A game starts single-player. Friends drop in and out at any time, and the host's save keeps their progress.

## Licensing

The engine is GPL version 2 or later, the same terms as UQM's code; the content stays CC BY-NC-SA 2.5, and the project must stay non-commercial. Matching UQM lets code flow both ways: AC2C can port UQM code, and UQM and its forks can take AC2C fixes. Users may still choose GPLv3, and builds that include libraries needing GPLv3 (such as Apache-2.0 ones) are released under v3. Godot is MIT, which is compatible.

| Part | License or owner | Rule for AC2C |
| --- | --- | --- |
| AC2C engine | GPLv2 or later | Offer source with every binary. Forks are allowed; pull requests are optional. |
| UQM source code | GPLv2 or later | May be read, ported and translated. Keep copyright notices on ported code. |
| UQM content: voiceovers, dialogue, graphics, sounds, music | CC BY-NC-SA 2.5 | Separate download. Credit, non-commercial, share-alike. |
| UQM documentation | CC BY 2.0 | Usable with credit. |
| "Star Control" trademark | Stardock | Never in names or marketing. |
| Star Control I and II universe, characters, story | Paul Reiche III and Fred Ford | Respect the content license; credit them. |
| "The Ur-Quan Masters" name | Licensed by Reiche and Ford to the fan project | Not the engine's name; "compatible with" is fine. |

### What lives where in UQM 0.8.0

Most gameplay data is in the GPL source, not the content package. Checked in the 0.8.0 source on 2026-10-31. Paths are relative to `sc2/` in the official repository (branch `v0.8.0`); see `docs/porting-inventory.md`.

| Data | Location | Notes |
| --- | --- | --- |
| Ship stats (crew, energy, thrust, mass, weapon cost) | `src/uqm/ships/<race>/<race>.c` | One file per ship, as `#define`s |
| Star map (502 hyperspace stars, plus 16 QuasiSpace entries) | `src/uqm/plandata.c` | `starmap_array` table |
| Special planets and systems | `src/uqm/planets/generate/gen*.c` | One generator per special system |
| Minerals, lifeforms, lander | `src/uqm/planets/` | `elemdata.h`, `lander.c` (`CreatureData`) |
| Story events and deadlines | `src/uqm/gameev.c` | Arilou arrival, Kohr-Ah victory and others |
| Fleet strengths and encounters | `src/uqm/grpinfo.c`, `encount.c` | |
| Conversation logic (branches, conditions, effects) | `src/uqm/comm/<race>/*.c` | The lines are content; the tree is code |
| Prices | e.g. `comm/melnorm/melnorm.c` | |
| Ship text, art, sounds, dialogue lines | Content package, by resource id | e.g. `ship.spathi.text`, `comm.spathi.dialogue` |

Reiche and Ford re-released the game as Free Stars: The Ur-Quan Masters in February 2024 and are developing a commercial sequel, Free Stars: Children of Infinity. Any commercial angle would conflict with them and with the content license.

Not legal advice. Consult a lawyer before public release.

## Co-op design decisions

Every rule below is decided. Values still to set are listed under Open items and should be built as settings.

| Topic | Decision | Implication |
| --- | --- | --- |
| Saving | Host keeps the save | Guests' ships live in the host's save |
| Drop-in / drop-out | Players join and leave at any time; a game can go solo → co-op → solo | No lobby-only start; joining is a normal in-game event |
| Joining | The first time a player joins a host's game, their new escort starts where the host is. Only the joining player sees a short scene of their ship undocking from the host's flagship; the host and other players aren't interrupted | Needs a basic escort loadout and an undocking scene (new; UQM has none). If the host isn't flying (battle, talk, orbit, planet, starbase), the escort appears at the host's position in the surrounding hyperspace or solar system. Rejoining players return where they left |
| Leaving | Ship, crew and cargo wait in the host's save until that player rejoins | Players need a stable identity per host save |
| Disconnect | Ship vanishes; returns on rejoin | Release any dialogue lock; a dead player's wreck stays |
| Disconnect mid-battle | Counts as a retreat, with the usual fuel cost | Never a loss, as in UQM. Also applies where UQM forbids retreat, because a disconnected ship has to leave |
| Game clock | One shared clock | Story deadlines and fleet movement are shared threats |
| Clock rate | Constant: one fixed rate (seconds per game day, a host setting) whatever players are doing. Only story pauses and the host's pause stop it | Simple and the same for solo and co-op; more players don't make deadlines come sooner. Differs from UQM, whose rate depends on the activity (see `docs/differences.md`) |
| Story conversations and events | Pause the game for everyone and show it to all players | Replaces the old "crucial events interrupt all" rule |
| Who replies in story talks | Whoever started it; others watch | Anyone can make any choice; it affects everyone |
| Dialogue lock | One player per alien race in conversation at a time | Applies to ordinary conversations |
| Pausing | Only the host can pause | Pause freezes combat for everyone |
| Ships | Host flies the flagship; others captain escorts | Escorts need their own landers, fuel and cargo |
| Escort abilities | Full: land, mine, talk to aliens | Escorts are effectively small flagships |
| Escort upgrades | Smaller module list at the starbase | Flagship keeps its unique upgrades |
| Host's AI fleet | Stays as in the original | Host can send one fleet ship into battle instead of the flagship |
| Key story items | Shared group inventory | Plot checks look at the group, not one ship |
| Resource units | Separate wallets, transfers allowed | Decide whether cargo can be traded too |
| Melnorme credits | Separate per player, like resource units; transferable at any time, including during conversations | A player trading with the Melnorme can be topped up by others mid-talk |
| Battle salvage | Fungible rewards split equally between the players in the battle when it's won. In UQM salvage is only resource units, paid per enemy ship destroyed | Retreated and disconnected players get none, as in UQM, where fleeing earns nothing |
| Targeting | Enemy ships, weapons and specials target the nearest opponent | Matches UQM's `TrackShip`, so most ship code ports unchanged |
| Planet landing | Players on the same planet share one landing screen, each with their own lander | Each mineral, bio or energy node can be taken once |
| Chat | Text chat between players, in every activity | New; UQM has none |
| Battles | A battle shows as a marker at its location in the solar system or in hyperspace, visible to the other players there. A player joins by flying into the marker, the same way an alien ship that reaches you starts an encounter. All players in the battle fight at once | "Nearby" means reaching the battle in flight; no fixed radius. Bigger arenas |
| Enemy side | One enemy ship at a time, as in the original | Players gang up on it |
| Enemy scaling | None; groups have it easier | Solo players keep original difficulty |
| Battle camera | Own camera per player, plus an arena minimap | Clients render only near their ship |
| Player death | Lose crew; ship becomes a wreck at the death location | Wreck persists for revival |
| Revival | A rescuer flies to the wreck and transfers crew, keeping at least 1 | |
| Flagship destroyed | Game over for the whole group | Flagship can revive escorts, so the group can't get stuck |
| Retreat | As in UQM 0.8.0: allowed only in encounters and the final battle, after the starbase is available, when not carrying the bomb. The ship warps out keeping its crew and can't rejoin that battle. When a player's last ship leaves, that player loses 5 fuel units, down to no lower than 0 | Each player retreats on their own. Host: AI fleet ships that retreat are out for that battle; the 5-unit cost applies when the flagship leaves |
| Player count | Up to 4 | Keeps hosting feasible on a home connection |

## Architecture

The host runs an authoritative server that owns all state; clients only render and send input. Players can be in different places at once, so the server runs one activity per player: hyperspace, a solar system, a planet surface, the starbase, a conversation or a battle.

```
Host machine
 ├─ Authoritative server  (world state, player state, one activity per player, combat)
 │    └─ Host save        (keeps away players' ships until they rejoin)
 │
 ├─ input / state ─ Host's own client
 ├─ input / state ─ Guest client
 ├─ input / state ─ Guest client
 └─ input / state ─ Guest client
                       (every client loads the UQM 0.8.0 content: art, audio, dialogue lines)
```

Guests connect over the internet to the host's server; the host plays through their own client like everyone else.

- **World state (server):** story flags, alliances, mined-out planets, the game date, alien fleet positions, the group inventory of key items, the host's AI fleet.
- **Player state (server):** stable player id, location, current activity, ship, modules, crew, fuel, cargo, wallet, alive or wreck. Saved in the host's save even while the player is away.
- **Client:** rendering, input, audio, the battle camera and minimap, player chat.
- **Planets:** generated deterministically from seeds, so the server sends seeds, not planet data.
- **Combat:** runs on the host's server, with clients sending inputs, since any fight can become multiplayer.
- **Story pause:** a story conversation or event moves every player into a shared "watching" state until it ends.

| Choice | Decision |
| --- | --- |
| Engine | Godot 4 with C#: built-in server-authoritative networking, headless dedicated server, MIT license |
| Platforms | Windows and Linux |
| Network | Friends over the internet, host at home; tune combat for typical home-internet lag |
| Networking approach | Keep it simple and cheap. Use Godot's built-in multiplayer (`MultiplayerAPI` with ENet, RPCs, `MultiplayerSynchronizer` / `MultiplayerSpawner`) the same way in every activity. The host runs the whole game; clients send input and show the state they receive. A small fixed input delay instead of client-side prediction, like UQM's Super Melee netplay (default 2 frames, about 83 ms at 24 fps); the delay is a setting. Clients are trusted: no anti-cheat, no lag compensation, no encryption. Requests (buy, transfer, pick up) go through the host only to keep one copy of the data |
| Content | Load the official UQM 0.8.0 content package from a path the player sets, or fetch it on first run (open item) |

## Fidelity and bugs

AC2C matches vanilla UQM 0.8.0 in solo play, except where co-op rules above change things.

- Keep gameplay quirks that players rely on.
- Fix crashes and clear bugs.
- Log every deliberate difference from 0.8.0 in `docs/differences.md`: what UQM does, what AC2C does, and why.
- When behaviour is unclear, the UQM source is the reference, checked by playing 0.8.0.

## Distribution and donations

The engine is free, with optional donations for development. Creative Commons Denmark's guidance treats voluntary donations as non-commercial and required payment as commercial. That is a regional interpretation, not an official ruling.

- Never gate anything behind donations: no donor-only features, early access or faster downloads.
- Ask for support of the engine work, not the content.
- Keep the content download separate: the official source, or fetched by the engine on first run.
- Use a separate donation platform (Ko-fi, GitHub Sponsors or Open Collective), linked rather than in the download flow.
- Credit Reiche, Ford and the UQM project on the download page and in the game.
- Give the engine an original name; describe it as compatible with The Ur-Quan Masters content.
- Publish the source under GPLv2 or later. Contributions are optional; say so in the README.
- Be open about AI-written code from the start.
- Talk to the UQM community before going public.

## Handover: first tasks for the code session

Start with a porting inventory, then build in four stages. Save this document in the repo as `docs/design.md` so every session sees it.

### Task 1: porting inventory

Clone vanilla UQM 0.8.0 next to the AC2C repo and write `docs/porting-inventory.md`, one row per system:

| Column | What goes in it |
| --- | --- |
| System | e.g. hyperspace travel, lander, Melnorme trading |
| UQM location | Source files and content resource ids |
| Kind | Data, logic, or both |
| Co-op change | Port as-is, adapt, or redesign |
| Notes | Single-player assumptions to remove, open questions |

Cover at least: ships and combat, hyperspace and QuasiSpace, solar systems and planets, lander and mining, starbase and shipyard, every race's conversations, story events and the clock, fleets and encounters, save and load, menus and HUD.

### Build order

1. Headless server with players flying around hyperspace together, joining and leaving at will.
2. Solar systems and planets from seeds.
3. Story flags, conversations, dialogue locks, story pauses and the group inventory.
4. Combat, including joining battles, one enemy at a time, and the minimap.

### Working rules

- The code is AI-written; the project owner playtests and reviews rather than writing code.
- Every change comes with automated tests that run headless (`dotnet test`, or Godot with `--headless`).
- Every change comes with a short "how to try it" note: what to run, what to do in game, what should happen.
- Port UQM data into readable data files (for example JSON), keeping GPL notices.
- Build every unset number as a setting.
- Keep `docs/design.md`, `docs/porting-inventory.md` and `docs/differences.md` current.

### Development setup

| Where | Setup | Used for |
| --- | --- | --- |
| Code session (cloud) | .NET SDK 8+, Godot 4 .NET build run headless | Writing code, `dotnet build`, headless tests and server runs |
| Owner's Linux machine | .NET SDK 8+, Godot 4 .NET editor, Git | Pulling changes, playing builds, checking scenes and errors |
| Owner's code editor | VSCodium with godot-tools and the free C# extension (muhammad-sammy, Open VSX) | Reading code and diffs, small fixes |

Bugs found in play go back to the code session with the error text, a screenshot and the expected behaviour.

## Open items

- [ ] Pick an original name for the engine
- [ ] Decide whether the engine downloads the UQM content itself or players point it at their own copy
- [ ] Set the constant clock rate (seconds per game day) by playtesting
- [ ] Decide whether a rescuer must keep more than 1 crew
- [ ] Decide whether cargo can be traded between players
- [ ] Decide who can use group devices (portal spawner, Sun device and others) and whether the group must be present (deferred)
- [ ] List the escort module set
- [ ] Mark which conversations and events count as story (pause for all)
- [ ] Draft the server data model and message protocol
- [ ] Contact the UQM community before going public
- [ ] Consult a lawyer before public release

## Sources

- [UQM 0.8.0 source mirror (MacSourcePorts/uqm)](https://github.com/MacSourcePorts/uqm): `COPYING` for the license split, `src/uqm/` for data locations
- [OpenRA discussion on GPL engine vs mod data](https://github.com/OpenRA/OpenRAModSDK/pull/82)
- [Godot docs: C# basics](https://docs.godotengine.org/en/4.2/tutorials/scripting/c_sharp/c_sharp_basics.html)
- [Godot docs: Compiling with .NET (4.5)](https://docs.godotengine.org/en/4.5/engine_details/development/compiling/compiling_with_dotnet.html)
- [free-vscode-csharp (C# extension for VSCodium)](https://github.com/muhammadsammy/free-vscode-csharp)
- From the original notes: Stardock Systems, Inc. v. Reiche (Wikipedia); Free Stars: Children of Infinity (Wikipedia); The Ur-Quan Masters (Wikipedia); Tetris Holding v. Xio Interactive (Game Developer); Creative Commons Denmark guidance on non-commercial use
