# Differences from UQM 0.8.0

Every deliberate difference between Attack on Crateris and vanilla UQM 0.8.0: what UQM does, what Crateris does, and why. Reference source: official UQM git, branch `v0.8.0`, commit `199e3748`; paths are relative to `sc2/`.

**Status:** *Planned* means decided in `docs/design.md` but not built yet. *Done* means built and covered by tests. Bug and crash fixes are listed here too.

## Co-op rules

| # | Topic | UQM 0.8.0 | Crateris | Why | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | When the game clock runs | Ticks only in hyperspace and QuasiSpace (5 s per day, `HYPERSPACE_CLOCK_RATE`) and in interplanetary flight (30 s per day, `INTERPLANETARY_CLOCK_RATE`): `src/uqm/starcon.c`, `src/uqm/planets/solarsys.c`. Stopped in battles, conversations, orbit, on planets and at the starbase | One shared clock at a constant rate (seconds per game day, a host setting), in every activity, solo or co-op. Only story conversations and events and the host's pause stop it | Per-activity rates make a group burn game days faster than a solo player, because someone is nearly always flying (including frequent QuasiSpace trips). A constant rate is simple and keeps deadlines the same for any group size. Applies in solo play too | Done in stage 1 (hyperspace): constant rate, default 5 s per day as a placeholder setting. Story pauses and the host's pause come later |
| 2 | Story conversations and events | Shown to the only player | Pause the game for everyone and show it to all; whoever started it chooses | Shared story, shared consequences | Planned |
| 3 | Time skips (starbase bomb install 14 days, Talking Pet) | `MoveGameClockDays` while the single player watches | Same skip, but as a story pause shown to every player | It moves time for the whole group | Planned |
| 4 | Retreat by disconnecting | Retreat (`RunAwayAllowed` / `DoRunAway` in `src/uqm/battle.c`, fuel cost in `src/uqm/pickship.c`) is allowed only in encounters and the last battle, after the starbase is available, when not carrying the bomb. Fleeing with the flagship as the last ship costs 5 fuel units, down to no lower than 0 | Retreat itself is unchanged and works per player. A player who disconnects mid-battle retreats automatically with the same fuel cost, even where UQM doesn't allow retreat | A disconnected player's ship has to leave the battle | Planned |
| 5 | Battle sides | 2 sides, 2 players (`NUM_SIDES 2`, `NUM_PLAYERS 2` in `src/uqm/init.h`); one ship per side at a time | Several player ships at once against one enemy ship at a time. How other players join is row 15 | Co-op combat | Planned |
| 6 | Battle camera | One camera zooms to frame both ships | Each player has their own camera on their ship, plus an arena minimap | Players can be far apart in a bigger arena | Planned |
| 7 | Arena size | Fixed | Bigger | Room for up to 4 player ships | Planned. Size to set |
| 8 | Losing a ship | Flagship destroyed is game over; escort losses are permanent | A player's ship becomes a wreck; a rescuer revives it by flying to it and transferring crew, keeping at least 1 for themselves. Only flagship destruction ends the game | Players shouldn't be knocked out of a shared game | Planned |
| 9 | Escorts | Combat-only ships in `built_ship_q` (max 12): no fuel, cargo or landers, flown only in battle | Guest players captain escorts that fly, land, mine and talk like small flagships, with a smaller module list. The host's AI fleet stays as in vanilla | Each guest needs a full ship of their own | Planned. Module list to set |
| 10 | Key story items | Device flags on the single flagship (`*_DEVICE` game states) | Shared group inventory; plot checks look at the group | Story items shouldn't be stuck with a player who left | Planned |
| 11 | Money | One resource-unit balance (`SIS_STATE.ResUnits`) | Separate wallet per player, transfers allowed | Each player earns and spends | Planned |
| 12 | Pause | The player pauses any time | Only the host can pause; pause freezes everyone, including combat | Fairness in shared play | Planned |
| 13 | Saves | Single-player save format (`src/uqm/save.c`) | New host save format holding world state and every player's state, including absent players | Drop-in / drop-out | Planned |
| 14 | Starting a ship | The one player starts the game at the starbase in the flagship | A guest joining a host's game for the first time gets a new escort at the host's position. Only the joining player sees a short scene of the escort undocking from the host's flagship; nobody else is interrupted. If the host isn't flying (battle, talk, orbit, planet, starbase), the escort appears at the host's position in the surrounding hyperspace or solar system. A rejoining player returns where they left | Puts a new player straight into the group's game, without a trip from the starbase | Done in stage 1, with a placeholder undocking scene (a 2-second message) and placeholder escort loadout |
| 15 | Joining a battle | A battle starts when an alien ship reaches the player (`encounter_collision` in `src/uqm/hyper.c`, `ip_group_collision` in `src/uqm/ipdisp.c`); nobody else can join | While a battle runs, a marker stays where it started, in the solar system or in hyperspace, visible to other players there. Flying into the marker joins the battle, the same way an alien ship starts an encounter. No fixed "nearby" radius | Lets the group help each other using a rule players already know | Planned |
| 16 | Leaving and disconnecting | One player; quitting ends the game | A player can leave at any time. Their ship, crew and cargo wait in the host's save; on disconnect the ship vanishes (mid-battle: row 4) and returns on rejoin. A dead player's wreck stays | Drop-in / drop-out | Done in stage 1 for hyperspace: a leaving player's ship stops and waits, and comes back stopped. Wrecks come with combat |
| 17 | Talking to aliens | One player talks to anyone | Ordinary conversations: one player per alien race at a time (dialogue lock), released on disconnect. Story conversations: row 2 | Stops two players changing one race's state at once | Planned |
| 18 | Battle salvage | `UninitEncounter` (`src/uqm/encount.c`): on victory only, the player gets `RACE_SHIP_COST / 8` resource units per enemy ship destroyed | Same amount, split equally between the players in the battle when it's won. Retreated and disconnected players get none | Fair split for a shared fight | Planned. Rounding of remainders to set |
| 19 | Melnorme credits | One balance, stored as game state (`MELNORME_CREDIT0`/`1` in `src/uqm/globdata.h`) | One balance per player, transferable between players at any time, including during a conversation | Each player sells their own bio data; others can top up a trader | Planned |
| 20 | Planet landing | One player, one lander | Players on the same planet share one landing screen, each with their own lander; each node is taken once | Lets players explore a planet together | Planned |
| 21 | Chat | None | Text chat between players, in every activity | Co-op needs a way to talk | Planned |
| 22 | Cargo trading | One ship holds all minerals and bio data (`ElementAmounts`, `TotalBioMass` in `SIS_STATE`) | Players in the same solar system can trade cargo, limited by the receiver's free storage | Lets a group pool minerals for one player to sell | Planned |

## Temporary differences

Differences that last only until a later stage provides what's missing. Numbered in the same series.

| # | Topic | UQM 0.8.0 | Crateris | Why | Status |
| --- | --- | --- | --- | --- | --- |
| 23 | Touching a star in hyperspace | Decided by sprite masks: the star's centre point against the flagship's hyperspace sprite, and, for the star just left, its whole image against the sprite (`hyper_collision`, `hyper_death`, `AddStars` in `src/uqm/hyper.c`). The masks come from the content package | Circles of about the sprites' size: the flagship 6 pixels, stars 4, 6 or 8 by size (`StarContactShape`). The other rules are UQM's: only while moving or steering, and not the star just left until the ship has cleared it | The masks are content, which isn't read until the content stage. A ship may enter a star a frame or two earlier or later than in UQM | Done in stage 2; replaced by the real masks in the content stage |
| 24 | Entering a star | The flagship spins and drifts onto the star for a few frames before the system loads (`init_transition`, `unhyper_transition`); the number of frames depends on the sprite's frame count | The server moves the ship into the system at once; the client can play the animation | The frame count is content. The ship arrives in the same place either way (row 23's note on timing applies) | Done in stage 2; the client animation comes with the content stage |

## Bug and crash fixes

None yet.

## Change history

Newest first. Row numbers stay fixed so review notes keep pointing at the right row.

| Date | Rows | Change |
| --- | --- | --- |
| 2026-11-01 | 23, 24 | Added temporary differences for entering stars until the content stage: circles instead of sprite masks, and no entry animation on the server |
| 2026-10-31 | 1, 14, 16 | Stage 1 built: constant clock, first join as an escort with undocking, leave and rejoin (ships wait stopped) |
| 2026-10-31 | 8, 22 | Revival: rescuer keeps at least 1 crew. Added cargo trading within the same solar system |
| 2026-10-31 | 18–21 | Added battle salvage (split equally), Melnorme credits (per player, transferable any time), shared planet landing and player chat. Targeting needs no row: nearest opponent is already UQM's behaviour |
| 2026-10-31 | 5, 14–17 | Added starting a ship (guest undocks from the host), joining a battle (fly into its marker), leaving and disconnecting, and the dialogue lock. Row 5 now points to row 15 for joining |
| 2026-10-31 | 4 | Retreat aligned with UQM: same rules and 5-unit fuel cost. Only difference left is that disconnecting counts as retreat. Corrected earlier text that said UQM retreat was free |
| 2026-10-31 | 1 | Game clock: one constant rate in every activity, replacing per-activity rates |
| 2026-10-31 | 1–13 | First version |
