# UQM data reference

The parts of UQM 0.8.0 that Attack on Crateris needs as data: where each part lives in the UQM source, what its fields mean, and which file in `data/` it becomes. It also lists the runtime structures the server's own data model replaces, and the parts that stay code or content.

Reference source: official UQM git (`https://git.code.sf.net/p/sc2/uqm`), branch `v0.8.0`, commit `199e3748`. Paths are relative to its `sc2/` directory. UQM's own developer notes in `sc2/doc/devel/` (CC BY 2.0) cover some of this in more depth; the useful ones are named below.

## How the data files are made

`tools/extract/extract_uqm.py` reads the UQM source and writes `data/*.json`. For every value it compiles a small C program against UQM's own headers and tables and prints the result, so macros, packed bit fields and enum values come from the C compiler. Only names, order and source comments are read as text.

Every file starts with `_notice` (GPL notice and UQM copyright), `_source` (repository, branch, commit, files) and often `_notes` (units and meanings). Don't edit the files by hand; change the extractor and run it again.

**Regenerate the data:**

```
git clone --depth 1 -b v0.8.0 https://git.code.sf.net/p/sc2/uqm ../uqm-0.8.0
python3 tools/extract/extract_uqm.py --uqm ../uqm-0.8.0/sc2 --out data
```

Needs Python 3 and gcc. Takes about 10 seconds.

**Check the data:**

```
python3 -m unittest discover -s tools/extract -v
UQM_SC2=../uqm-0.8.0/sc2 python3 -m unittest discover -s tools/extract -v
```

The first runs 21 checks on the committed files (counts, known values, notices). The second also re-runs the extractor and confirms the committed files match the source exactly.

## Units

UQM mixes several unit systems. The engine keeps UQM's units internally, so ported formulas stay exact, and converts only for display.

| Quantity | UQM unit | Conversion | Source |
| --- | --- | --- | --- |
| Hyperspace position | Universe units, 0 to 9999 on each axis | Shown to the player divided by 10: Sol 1752, 1450 is 175.2, 145.0 | `units.h` (`MAX_X_UNIVERSE`) |
| Combat position, speed, range | World units | `DISPLAY_TO_WORLD (x)` is `x << 2` | `units.h` |
| Time | `ONE_SECOND` = 840 ticks | A battle frame is 35 ticks, so 24 frames per second (`BATTLE_FRAME_RATE`) | `libs/timelib.h`, `battle.h` |
| Ship waits (turn, thrust, weapon, energy) | Battle frames | Divide by 24 for seconds | ship files |
| Fuel | 1/100 of a fuel unit (`FUEL_TANK_SCALE` 100) | 1000 is the 10 units shown | `sis.h` |
| Module prices | Units of 50 resource units (`MODULE_COST_SCALE`) | Price = cost x 50 | `sis.h` |
| Game clock | Seconds of real time per game day | Hyperspace 5, solar system 30 in UQM; Crateris uses one constant rate | `clock.h` |

## Data files

### `starmap.json`: stars and QuasiSpace

From `starmap_array` in `src/uqm/plandata.c` (type `STAR_DESC`, `src/uqm/planets/planets.h`).

| UQM field | JSON | Meaning |
| --- | --- | --- |
| `star_pt` | `x`, `y` | Position in universe units |
| `Type` (packed) | `size`, `color` | `STAR_TYPE` gives `DWARF_STAR`, `GIANT_STAR` or `SUPER_GIANT_STAR`; `STAR_COLOR` gives `BLUE_BODY` and so on (`planets/plandata.h`) |
| `Index` | `special` | Special-system generator (`*_DEFINED` in `gendef.h`), or null for an ordinary system |
| `Prefix`, `Postfix` | `name_prefix`, `name_postfix` | Indexes into the content's star name strings (see Content below) |

502 hyperspace stars, then 16 QuasiSpace entries (15 portal exits and the Arilou homeworld). A system's random seed is `MAKE_DWORD (x, y)` (`GetRandomSeedForStar`, `planets/solarsys.c`), so the planets of every system follow from this file plus the generator code.

**Used by:** hyperspace and the star map (stage 1), solar systems (stage 2).

### `planet-types.json`: the 59 planet types

From `planet_array` in `src/uqm/plandata.c` (type `PlanetFrame`, `planets/plandata.h`). One entry per planet type, not per planet.

| UQM field | JSON | Meaning |
| --- | --- | --- |
| `Type` (packed) | `size`, `map_algorithm`, `color` | Small rocky, large rocky or gas giant; how the surface map is drawn; colour in a solar system |
| `BaseTectonics` | `base_tectonics` | Earth is 100 |
| `AtmoAndDensity` (packed) | `density_class`, `atmosphere_class` | Low and high nibble |
| `UsefulElements[8]` | `useful_elements` | Up to 8 minerals: element, quantity class (`FEW`, `MODERATE`, `NUMEROUS`), quality class (`LIGHT`, `MEDIUM`, `HEAVY`) |
| `num_faults`, `fault_depth`, `num_blemishes`, `base_elevation` | same names | Surface map generator parameters |
| `CMapInstance`, `XlatTabInstance` | `color_map_resource`, `color_table_resource` | Content resource ids for the planet's colours |

Individual planets come from `planets/plangen.c`, `calc.c`, `gentopo.c` and `orbits.c` (code). `PLANET_INFO` (`plandata.h`: tilt, tectonics, weather, gravity, temperature and so on) is computed per planet by `calc.c`, not stored.

**Used by:** solar systems and planets (stage 2), lander and mining.

### `elements.json`: minerals

From `element_array` in `src/uqm/plandata.c` and the enums in `src/uqm/planets/elemdata.h`. 133 elements, each in one of 8 categories (`COMMON` to `EXOTIC`). `worth` is the starting resource-unit value per unit of cargo in each category, set in `InitGameStructures` (`globdata.c`).

### `lifeforms.json`: creatures

From `CreatureData` in `src/uqm/planets/lander.c` (type `LIFEFORM_DESC`, `planets/lifeform.h`). 23 ordinary creatures and 3 special ones (Evil One, Brainbox Bulldozers, Zex's Beauty).

| UQM field | JSON | Meaning |
| --- | --- | --- |
| `Attributes` (packed) | `behavior`, `awareness`, `speed`, `danger` | Hunt, flee or unpredictable; low to high; motionless to fast; harmless to monstrous |
| `ValueAndHitPoints` (packed) | `bio_value`, `hit_points` | Low nibble: bio data per creature. High nibble: stun hits to capture |
| (source comment) | `label` | Creature name from the source comment; display names are content |

### `ships.json`: all 28 ships

From the `RACE_DESC` initialiser in each `src/uqm/ships/<dir>/<dir>.c` (types in `races.h`).

| UQM structure | JSON | Meaning |
| --- | --- | --- |
| `SHIP_INFO` | `flags`, `cost`, `crew`, `max_crew`, `energy`, `max_energy` | `flags`: firing directions and behaviour hints (`FIRES_FORE`, `SEEKING_WEAPON`, `POINT_DEFENSE` and others). `cost` is the Super Melee point cost |
| `FLEET_STUFF` | `fleet.strength`, `fleet.home_x`, `fleet.home_y` | Starting size and centre of the race's sphere of influence |
| `CHARACTERISTIC_STUFF` | `characteristics.*` | Thrust, energy regeneration, weapon and special energy cost, waits (in frames), mass |
| `INTEL_STUFF` | `ai.maneuverability_index`, `ai.weapon_range` | Inputs to the combat AI |
| `#define`s in the file | `defines` | Every integer constant in the ship's source, including weapon speed, life, damage and range, which are not in `RACE_DESC` |
| (`loadship.c`) | `species` | The `SPECIES_ID` that loads this ship |

Quirk to keep: UQM's `MAX_CREW` define is the *starting* crew. Most ships start full, but the Syreen start with 12 of 42.

Includes the flagship (`sis_ship`; its real stats come from installed modules), the Sa-Matra (`lastbat`) and the Slylandro probe (`probe`). What weapons and specials *do* is code (`*_preprocess`, `*_postprocess`, collision and AI functions in the same files) and is ported by hand, one C# class per ship.

**Used by:** combat (stage 4), fleets and encounters.

### `races.json`: per-race tables

From `races.h`: the `*_SHIP` enum (the order of `avail_race_q`, one `FLEET_INFO` per race), `RACE_SHIP_COST` (battle salvage is cost / 8 per enemy ship destroyed), `RACE_IP_SPEED` (speed in solar systems) and `RACE_COMMUNICATION` (which conversation a race uses). 23 races have a cost; the last entry (drone/Sa-Matra) doesn't.

### `game-state.json`: story flags

From the `ADD_GAME_STATE` list in `src/uqm/globdata.h`. 447 named flags and counters packed into 1,238 bits: `name`, `bits`, `start_bit`, the section heading above it (`group`) and its source comment where there is one. UQM reads them with `GET_GAME_STATE` / `SET_GAME_STATE`.

In Crateris these are world state, read and written by name, not by bit position. The bit layout is kept only for importing UQM saves, if that's ever done.

**Used by:** conversations, story events, generators (stage 3), and anything else that checks the story.

### `events.json` and the clock

`events.json` lists the 18 clock events from `gameev.h` (Arilou entrance and exit, Kohr-Ah victory and genocide, the Pkunk, Thraddash, Ilwrath, Mycon and Utwig/Supox missions and others). What each event does is code in `EventHandler` (`gameev.c`).

The clock itself is `CLOCK_STATE` (`clock.h`): day, month, year, ticks, and a queue of `EVENT`s (date plus event id). Events are absolute (a date) or relative (days from now). See `doc/devel/timing`.

### `new-game.json`: starting values

From `InitGameStructures` (`globdata.c`), `InitGameClock` (`clock.c`) and `AddInitialGameEvents` (`gameev.c`):

- **Start date:** 17 February 2155.
- **Prices:** crew 3, fuel 20 resource units; module costs and mineral worth.
- **Flagship:** 50 crew, 10 fuel units, 1 lander, 0 resource units, at Sol, with two fusion thrusters, two turning jets, a fuel tank, a crew pod, a storage bay and a gun.
- **Initial events:** hyperspace encounters start being generated after 1 day; Arilou entrance on 17 March 2155; Kohr-Ah victory after 4 years; Slylandro probes start ramping up.

### `modules.json`: flagship modules

The module enum from `sis.h`: lander, thruster, jets, crew pod, storage bay, fuel tank, high-efficiency fuel system, dynamo, Shiva furnace, gun, blaster, cannon, tracking system, point defence, and the 6 bomb parts. `start_cost` is the price at the start (0 means not for sale yet; the rest become available through the story).

**Used by:** outfitting, and choosing the escort module set (an open item).

### `constants.json`: limits and rates

Named constants from UQM headers: clock rates, slot counts, capacities, fuel and cost scales, universe size, Sol, QuasiSpace and Arilou positions, queue limits, and two constants defined inside functions (`RUN_AWAY_FUEL_COST` 500 in `pickship.c`, `LOST_DAYS` 14 in `starbase.c`).

## Runtime state: what the server's data model replaces

These UQM structures hold the game while it runs and in save files. They aren't data files; they're the starting point for the server's world state and player state. `doc/devel/savefile` and `doc/devel/statefiles` describe how UQM saves them.

| UQM structure | Where | Holds | In Crateris |
| --- | --- | --- | --- |
| `SIS_STATE` | `sis.h` | The flagship: position, resource units, fuel, crew, module, drive and jet slots, landers, cargo, names | Player state, one per player. The host's is the flagship; guests' escorts use the same shape with a smaller module list |
| `GAME_STATE` | `globdata.h` | Everything else: prices, current activity, clock, autopilot, location in a solar system, the queues below, story bits | Split into world state (prices, clock, queues, story) and per-player state (activity, location, autopilot) |
| `CLOCK_STATE` + `EVENT` queue | `clock.h` | Date and scheduled story events | World state, one shared clock |
| `FLEET_INFO` (`avail_race_q`) | `races.h` | Per race: allied or not, sphere of influence (centre, strength, growth, destination), what the player knows of it | World state |
| `SHIP_FRAGMENT` (`built_ship_q`) | `races.h` | The flagship's escort ships (max 12): race, crew | The host's AI fleet |
| `SHIP_FRAGMENT` (`npc_built_ship_q`) | `races.h` | The enemy ships of the current encounter | Per battle |
| `ENCOUNTER` (`encounter_q`) | `encount.h` | Hyperspace encounters (the black globes): race, ship count, position (max 16) | World state, shared by players in hyperspace |
| `IP_GROUP` (`ip_group_q`) | `grpinfo.h` | Alien groups in the current solar system: race, task, destination, orbit, position | Per solar system, shared by the players there |
| `PLANET_DESC`, `SOLARSYS_STATE` | `planets/planets.h` | The generated planets and moons of the current system | Generated per system from the seed; not saved |
| Star info state file | `state.c`, `doc/devel/statefiles` | Per star: which mineral, energy and life nodes have been taken on each world (one bit each) | World state, so a second player sees the first one's work |
| Battle group state files | `grpinfo.c`, `doc/devel/statefiles` | Random and scripted groups per system (`DEFGRP_*`) | World state |

## Code, not data

These are logic and get ported by hand to C#, using the data files above. Where they hold numbers, the numbers stay in the code next to the logic.

| What | Where |
| --- | --- |
| Hyperspace movement, encounters, QuasiSpace | `hyper.c`, `encount.c` |
| Solar system flight, orbits, NPC groups | `planets/solarsys.c`, `orbits.c`, `ipdisp.c` |
| Planet and surface generation | `planets/plangen.c`, `calc.c`, `gentopo.c`, `surface.c`, `scan.c` |
| Special systems | `planets/generate/gen*.c` (28 plus `gendefault.c`) |
| Lander | `planets/lander.c` |
| Combat engine and ship behaviour | `battle.c`, `process.c`, `collide.c`, `gravity.c`, `velocity.c`, `weapon.c`, `tactrans.c`, `ships/*/` |
| Combat AI | `cyborg.c`, `intel.c`, `*_intelligence` in ship files |
| Conversation trees | `comm/<race>/*.c` (27 scripts) |
| Story events | `gameev.c` |
| Starbase, outfitting, shipyard | `starbase.c`, `outfit.c`, `shipyard.c` |
| Random numbers | `src/libs/math/random.c`, `random2.c` (Park–Miller, A = 16807, M = 2^31 - 1); must match exactly |

## Content, not ours

The content package (CC BY-NC-SA 2.5) is downloaded on first run and read by the client. The data files refer to it only by index or resource id:

- **Star names:** `name_postfix` indexes the game strings (`content/base/gamestrings.txt`, `STAR_STRING_BASE`); `name_prefix` 0 means none, otherwise index - 1 into the Greek-letter numbers at `STAR_NUMBER_BASE` (`GetClusterName`, `starmap.c`).
- **Ship and race text, art, sounds:** `race_strings_resource` and the `ship.<name>.*` ids in `content/uqm.rmp`.
- **Planet colours:** `color_map_resource`, `color_table_resource`.
- **Dialogue lines:** referenced by phrase id from the conversation scripts.
- **Creature, module, element and device names:** content strings.

## Not extracted yet

- **Scripted battle groups (`DEFGRP_*`) and per-race encounter makeup:** built in code (`grpinfo.c`, `build.c`, generators); stage 2 and 3.
- **Device list (`*_DEVICE`):** these are story flags, already in `game-state.json`; their effects are code in `planets/devices.c`.
- **Melnorme prices and sales:** in `comm/melnorm/melnorm.c`, ported with the conversation.
- **Sun data (`sundata.h`):** star size and energy constants, small enough to port with the generator.
