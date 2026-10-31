# Differences from UQM 0.8.0

Every deliberate difference between AC2C and vanilla UQM 0.8.0: what UQM does, what AC2C does, and why. Reference source: official UQM git, branch `v0.8.0`, commit `199e3748`; paths are relative to `sc2/`.

**Status:** *Planned* means decided in `docs/design.md` but not built yet. *Done* means built and covered by tests. Bug and crash fixes are listed here too.

## Co-op rules

| # | Topic | UQM 0.8.0 | AC2C | Why | Status |
| --- | --- | --- | --- | --- | --- |
| 1 | When the game clock runs | Ticks only in hyperspace and QuasiSpace (5 s per day, `HYPERSPACE_CLOCK_RATE`) and in interplanetary flight (30 s per day, `INTERPLANETARY_CLOCK_RATE`): `src/uqm/starcon.c`, `src/uqm/planets/solarsys.c`. Stopped in battles, conversations, orbit, on planets and at the starbase | One shared clock at a constant rate (seconds per game day, a host setting), in every activity, solo or co-op. Only story conversations and events and the host's pause stop it | Per-activity rates make a group burn game days faster than a solo player, because someone is nearly always flying (including frequent QuasiSpace trips). A constant rate is simple and keeps deadlines the same for any group size. Applies in solo play too | Planned. Rate to set by playtesting |
| 2 | Story conversations and events | Shown to the only player | Pause the game for everyone and show it to all; whoever started it chooses | Shared story, shared consequences | Planned |
| 3 | Time skips (starbase bomb install 14 days, Talking Pet) | `MoveGameClockDays` while the single player watches | Same skip, but as a story pause shown to every player | It moves time for the whole group | Planned |
| 4 | Retreat by disconnecting | Retreat (`RunAwayAllowed` / `DoRunAway` in `src/uqm/battle.c`, fuel cost in `src/uqm/pickship.c`) is allowed only in encounters and the last battle, after the starbase is available, when not carrying the bomb. Fleeing with the flagship as the last ship costs 5 fuel units, down to no lower than 0 | Retreat itself is unchanged and works per player. A player who disconnects mid-battle retreats automatically with the same fuel cost, even where UQM doesn't allow retreat | A disconnected player's ship has to leave the battle | Planned |
| 5 | Battle sides | 2 sides, 2 players (`NUM_SIDES 2`, `NUM_PLAYERS 2` in `src/uqm/init.h`); one ship per side at a time | Several player ships at once against one enemy ship at a time. A running battle shows as a marker where it started; other players join by flying into it, like an encounter | Co-op combat | Planned |
| 6 | Battle camera | One camera zooms to frame both ships | Each player has their own camera on their ship, plus an arena minimap | Players can be far apart in a bigger arena | Planned |
| 7 | Arena size | Fixed | Bigger | Room for up to 4 player ships | Planned. Size to set |
| 8 | Losing a ship | Flagship destroyed is game over; escort losses are permanent | A player's ship becomes a wreck; a rescuer revives it by transferring crew. Only flagship destruction ends the game | Players shouldn't be knocked out of a shared game | Planned |
| 9 | Escorts | Combat-only ships in `built_ship_q` (max 12): no fuel, cargo or landers, flown only in battle | Guest players captain escorts that fly, land, mine and talk like small flagships, with a smaller module list. The host's AI fleet stays as in vanilla | Each guest needs a full ship of their own | Planned. Module list to set |
| 10 | Key story items | Device flags on the single flagship (`*_DEVICE` game states) | Shared group inventory; plot checks look at the group | Story items shouldn't be stuck with a player who left | Planned |
| 11 | Money | One resource-unit balance (`SIS_STATE.ResUnits`) | Separate wallet per player, transfers allowed | Each player earns and spends | Planned |
| 12 | Pause | The player pauses any time | Only the host can pause; pause freezes everyone, including combat | Fairness in shared play | Planned |
| 13 | Saves | Single-player save format (`src/uqm/save.c`) | New host save format holding world state and every player's state, including absent players | Drop-in / drop-out | Planned |

## Bug and crash fixes

None yet.
