# Porting inventory: UQM 0.8.0 → Attack on Crateris

One row per UQM system: where it lives, what kind of thing it is, and what co-op does to it. See `docs/design.md` for the rules referred to here and `docs/differences.md` for the log of deliberate behaviour changes.

## Reference source

- **Repository:** official UQM git, `https://git.code.sf.net/p/sc2/uqm`, branch `v0.8.0`, commit `199e3748cc41b688a65601e3b7b2a74bdb2c917c` (2020-12-26).
- **Paths** below are relative to `sc2/` in that repository (so `src/uqm/hyper.c` is `sc2/src/uqm/hyper.c`). The MacSourcePorts mirror named in `docs/design.md` has the same layout at its root, with later macOS patches on top; use the official branch as the reference.
- **Content:** `sc2/content/` in the same repository holds the base content tree (about 12,800 files). The resource map `content/uqm.rmp` maps 963 resource ids to files. Types: `GFXRES` 582, `BINTAB` 149, `MUSICRES` 64, `STRTAB` 51, `FONTRES` 32, `SNDRES` 30, `SHIP` 28, `CONVERSATION` 27.
- Surveyed 2026-10-31. Line counts and counts of `SET_GAME_STATE` calls are from that commit.

The data parts are extracted to `data/` by `tools/extract/extract_uqm.py`; `docs/uqm-data.md` describes each file and the UQM structures behind it.

Columns follow the handover: **Kind** is Data, Logic, or Both. **Co-op change** is *Port as-is*, *Adapt* (same rules, with the single-player assumptions removed) or *Redesign* (new rules from `docs/design.md`).

## Cross-cutting single-player assumptions

These turn up in almost every row. Remove them once, in the core, not system by system.

| Assumption in UQM | Where | What Crateris needs |
| --- | --- | --- |
| One global state: `GLOBDATA GlobData` = `SIS_STATE` (the flagship) + `GAME_STATE` (everything else), reached through the `GLOBAL()` / `GLOBAL_SIS()` macros | `src/uqm/globdata.h`, `globdata.c` | Split into **world state** (story bits, clock, fleets, group inventory) and **player state** (one per player id: ship, location, activity, cargo, wallet) |
| One current activity: `GLOBAL (CurrentActivity)` (`IN_HYPERSPACE`, `IN_INTERPLANETARY`, `IN_ENCOUNTER`, `IN_LAST_BATTLE`, …) run by the blocking loop in `starcon.c` | `src/uqm/starcon.c`, `globdata.h` | One activity object per player, all stepped by the server tick; no blocking loops |
| Blocking, frame-driven UI loops (`DoInput` with per-screen `InputFunc` callbacks) that mix game rules with drawing | Almost every gameplay file | Separate the rules (server, testable headless) from the presentation (Godot client) |
| One global RNG `TFB_Random()` (Park–Miller, `A = 16807`, `M = 2^31 − 1`), used about 70 times outside the system generator | `src/libs/math/random.c`, `src/uqm/util.c` | A separate RNG per activity, so one player's actions don't change another's rolls. Keep the same algorithm where determinism matters (planets) |
| Story state as a packed bit array: 447 `ADD_GAME_STATE` fields (1,238 bits) | `src/uqm/globdata.h` | Port the field list to a data file. Read and write by name. Lives in world state |
| Battles are 2-sided, 2 players: `NUM_PLAYERS 2`, `NUM_SIDES 2`, `MAX_SHIPS_PER_SIDE 14` | `src/uqm/init.h`, `races.h` | N human ships on one side, one enemy ship at a time on the other |
| Escorts are combat-only `SHIP_FRAGMENT`s in `built_ship_q` (max 12, `MAX_BUILT_SHIPS`), with no fuel, cargo or landers | `src/uqm/sis.h`, `shipyard.c` | Keep this for the host's AI fleet. Player escorts are a new kind of ship with flagship-like state |

## Inventory

### Engine foundations

| System | UQM location | Kind | Co-op change | Notes |
| --- | --- | --- | --- | --- |
| Content loading (resource map, resource types) | `src/libs/resource/`, `content/uqm.rmp`, `content/uqm.key`, `content/menu.key` | Both | Adapt | Client-only. Write a C# reader for `.rmp` (`id = TYPE:path`) and the formats we use (`.ani`, `.ct`, `.txt` string tables, `.mod`/`.ogg`, `.fon`). The server needs only `SHIP` codes and string ids, not art. Where content comes from (download or player's path) is an open item |
| Random numbers | `src/libs/math/random.c`, `random2.c` (`RandomContext`), `src/uqm/util.c` | Logic | Adapt | Port Park–Miller exactly: planets depend on it bit for bit. One context per activity, plus a world context for events |
| Main loop and activity switching | `src/uqm/starcon.c`, `restart.c`, `init.c` | Logic | Redesign | Becomes the server's per-player activity state machine. `restart.c` (new game and main menu) becomes host-side game start |
| World and global state | `src/uqm/globdata.c`, `globdata.h`, `state.c`, `state.h` | Both | Redesign | Split into world and player state (see above). `state.c` keeps per-star planet info (mined-out nodes, scanned flags) in in-memory "state files": move that to world state |
| Game state bit table | `src/uqm/globdata.h` (`ADD_GAME_STATE`, 447 fields, 1,238 bits) | Data | Port as-is | Extracted to `data/game-state.json` (name, bits, start bit, group, source comment); see `docs/uqm-data.md` |
| Input and controls | `src/uqm/gameinp.c`, `controls.h`, `battlecontrols.c`, `src/libs/input/` | Logic | Adapt | Client sends input frames to the server. Key bindings come from Godot's input map. `BATTLE_ESCAPE` triggers retreat |
| Audio | `src/uqm/sounds.c`, `src/libs/sound/` | Logic | Adapt | Client-only. Godot plays `.ogg`; `.mod` music needs a tracker player: any GPL-compatible one that works with Godot C#, chosen when audio is built |
| Debug tools | `src/uqm/uqmdebug.c` | Logic | Adapt | Useful for test fixtures (dump a star system, set states, move the clock) |

### Ships and combat

| System | UQM location | Kind | Co-op change | Notes |
| --- | --- | --- | --- | --- |
| Ship stats | `src/uqm/ships/<race>/<race>.c` (`#define MAX_CREW`, `MAX_ENERGY`, `ENERGY_REGENERATION`, `ENERGY_WAIT`, `MAX_THRUST`, `THRUST_INCREMENT`, `TURN_WAIT`, `SHIP_MASS`, `WEAPON_ENERGY_COST`, weapon speed, life and damage). Resource ids `ship.<race>.*` | Data | Port as-is | 28 ships: 25 race ships, plus `probe` (Slylandro), `sis_ship` (flagship) and `lastbat` (Sa-Matra). About 13,900 lines in all. Extracted to `data/ships.json` (all 28, including every integer `#define`); see `docs/uqm-data.md` |
| Ship behaviour (weapons, specials, per-ship preprocess and postprocess) | Same files: `initialize_*`, `*_preprocess`, `*_postprocess`, `*_intelligence` | Logic | Adapt | One C# class per ship. Targeting already works with several ships: homing and turning code calls `TrackShip` (`src/uqm/weapon.c`), which picks the nearest enemy ship (distance approximated as \|dx\| + \|dy\|), and area effects such as the Syreen song loop over every enemy ship in range. Keep this as-is; check each ship for code that assumes exactly one opponent |
| Flagship in combat | `src/uqm/ships/sis_ship/sis_ship.c` | Both | Adapt | Weapons, thrust and crew come from the installed modules. Player escorts may reuse this with the smaller module list |
| Combat engine: elements, movement, collisions, gravity, display list | `src/uqm/battle.c`, `process.c`, `collide.c`, `gravity.c`, `velocity.c`, `weapon.c`, `element.h`, `displist.c`, `ship.c` | Logic | Adapt | Runs on the server at `BATTLE_FRAME_RATE` (24 fps). Fixed-point world coordinates: keep them, for determinism. Arena size is fixed today; needs to be bigger. Clients render only near their ship |
| Battle start and fleets (who fights) | `src/uqm/encount.c` (`BuildBattle`, `InitEncounter`, `EncounterBattle`, `UninitEncounter`), `build.c`, `loadship.c`, `init.c` | Logic | Redesign | Other players join by flying into the battle's marker (see the encounter rows). Enemy side sends one ship at a time. `UninitEncounter` pays salvage only on victory: for each enemy ship destroyed, `RACE_SHIP_COST` of that race divided by 8, in resource units (e.g. Ur-Quan 3000 → 375 RU). Crateris splits it equally between the players in the battle when it's won |
| Ship selection and death | `src/uqm/pickship.c`, `tactrans.c` (`new_ship`, `ship_death`, `cleanup_dead_ship`) | Logic | Redesign | Players no longer pick the next ship from a fleet. A dead player's ship becomes a wreck. Host can send one ship from the AI fleet instead of the flagship. Flagship death ends the game for everyone |
| Retreat | `src/uqm/battle.c` (`RunAwayAllowed`, `DoRunAway`), `tactrans.c` (`flee_preprocess`, `cleanup_dead_ship`), `pickship.c` (`RUN_AWAY_FUEL_COST`) | Logic | Port as-is | Vanilla: allowed only in encounters or the last battle, after the starbase is available and when not carrying the bomb. The ship flashes red, warps out keeping its crew and is removed from that battle's count (`battle_counter`), so it can't be picked again. If the flagship is the last ship to leave, the player loses `RUN_AWAY_FUEL_COST` (`5 * FUEL_TANK_SCALE`, shown as 5 units), down to no lower than 0. Crateris: same, per player. Disconnect counts as retreat, even where retreat isn't allowed |
| Combat AI | `src/uqm/cyborg.c`, `intel.c`, per-ship `*_intelligence` | Logic | Adapt | The enemy AI targets the nearest opponent, matching `TrackShip`; check `cyborg.c` for places that assume one opponent. The host's AI-flown fleet ships use the same code |
| Battle camera, minimap | `src/uqm/battle.c`, `displist.c` (zooming camera between the 2 ships) | Logic | Redesign | Each client has its own camera on its ship, plus an arena minimap |
| Combat netplay (Super Melee only) | `src/uqm/supermelee/netplay/` (lockstep input, checksums, CRC) | Logic | Reference only | Not ported: Crateris uses server-authoritative state, not lockstep. Useful as a reference for checksums in tests |
| Super Melee mode | `src/uqm/supermelee/` | Both | Out of scope | Possibly later; not in the build order |

### Hyperspace, QuasiSpace and the star map

| System | UQM location | Kind | Co-op change | Notes |
| --- | --- | --- | --- | --- |
| Star map data | `src/uqm/plandata.c` (`starmap_array`), `starmap.h` (`NUM_SOLAR_SYSTEMS 502`) | Data | Port as-is | 502 hyperspace stars, then a sentinel, then 15 QuasiSpace portal exits and 1 more entry (the Arilou homeworld), then a sentinel. Each entry: position, `MAKE_STAR (size, colour, -1)`, special-system id (`*_DEFINED`), postfix and prefix of the name. Extracted to `data/starmap.json` |
| Star names | Content: `STRTAB` star name tables, indexed by the `starmap_array` prefix and postfix | Data | Port as-is | Content, not code: the client looks them up |
| Hyperspace flight | `src/uqm/hyper.c` (`MoveSIS`, `LoadHyperspace`, `hyper_transition`, `InterplanetaryTransition`), `velocity.c`, `gravity.c` | Logic | Adapt | First thing to build (build order 1). Every player has their own ship in the same hyperspace. Fuel use per player |
| Hyperspace encounters and NPC fleets | `src/uqm/hyper.c` (`check_hyperspace_encounter`, `AddEncounterElement`, `ProcessEncounter`, `SeedUniverse`), `encount.c`, `globdata.h` (`encounter_q`, `MAX_ENCOUNTERS 16`) | Logic | Adapt | Encounter generation ties to "the" player's position. With several players: spawn around each player, shared queue in world state. The player who collides starts the encounter. While it runs, a battle marker stays at that spot; another player who flies into it joins the battle, reusing the encounter collision (`encounter_collision`) |
| Race fleets and spheres of influence | `src/uqm/grpinfo.c`, `build.c`, `gameev.c` (`SetRaceDest`, `check_race_growth`), `avail_race_q` (`FLEET_INFO`) | Both | Port as-is | World state. Fleet movement runs on the shared clock |
| QuasiSpace and Arilou portals | `src/uqm/hyper.c` (`ArilouSpaceTransition`, `arilou_space_*`), `hyper.h` (`ARILOU_SPACE_X/Y`, `QUASI_SPACE_X`), game states `ARILOU_SPACE`, `ARILOU_SPACE_SIDE`, `ARILOU_SPACE_COUNTER`, `PORTAL_SPAWNER_DEVICE` | Both | Adapt | The portal's open/closed state is world state (shared). Which side of the portal each player is on is player state. The portal spawner is a key item: group inventory |
| Star map screen and search | `src/uqm/starmap.c`, `planets/pstarmap.c` (zoom, autopilot, `F6`/`/` search) | Logic | Adapt | Client UI. Autopilot target is per player. Show other players' positions |
| Hyperspace menu (save, devices, cargo, roster, starmap) | `src/uqm/hyper.c` (`DoHyperspaceMenu`), `gameopt.c`, `planets/devices.c`, `cargo.c`, `roster.c` | Logic | Adapt | Per player. Saving is host-only |

### Solar systems and planets

| System | UQM location | Kind | Co-op change | Notes |
| --- | --- | --- | --- | --- |
| Interplanetary flight | `src/uqm/planets/solarsys.c` (`ExploreSolarSys`, `LoadSolarSys`), `orbits.c`, `ipdisp.c` (`ip_group_q`: NPC ships in the system; `ip_group_preprocess` moves groups, intercepting the flagship when hostile, `ip_group_collision` / `flag_ship_collision` start the encounter) | Logic | Adapt | Several players can be in one system. NPC groups and their orbits are shared; an intercepting group needs a target rule, e.g. the nearest player. A running battle shows as a marker at its spot; flying into it joins the battle, like `ip_group_collision` |
| System and planet generation | `src/uqm/planets/plangen.c`, `gentopo.c`, `calc.c`, `orbits.c`, `pl_stuff.c`, `plandata.h`, `sundata.h`, `planets/generate/gendefault.c` | Logic | Port as-is | Deterministic: seed is `MAKE_DWORD (star.x, star.y)` (`GetRandomSeedForStar`), each planet has `rand_seed`. Port the RNG and generator bit for bit and test against output from UQM's debug dump |
| Planet type table, minerals | `src/uqm/plandata.c` (`planet_array[NUMBER_OF_PLANET_TYPES]`, `element_array`, `MINERAL_DEPOSIT`), `planets/elemdata.h` | Data | Port as-is | Extracted to `data/planet-types.json` and `data/elements.json` |
| Special systems | `src/uqm/planets/generate/gen*.c`: `genand` (Androsynth), `genburv` (Burvixese), `genchmmr`, `gencol` (colony), `gendru`, `genilw`, `genmel` (Melnorme), `genmyc`, `genorz`, `genpet` (Talking Pet), `genpku`, `genrain` (rainbow worlds), `gensam` (Sa-Matra), `genshof`, `gensly`, `gensol` (Sol), `genspa`, `gensup`, `gensyr`, `genthrad`, `gentrap` (Trappist), `genutw`, `genvault`, `genvux`, `genwreck`, `genyeh`, `genzfpscout`, `genzoq` | Both | Adapt | 28 special generators, plus `gendefault.c` for ordinary systems. They mix data (special planets, artifacts) with story hooks (`GenerateFunctions` in `planets/generate.h`: `generatePlanets`, `generateOrbital`, `generateLife`, `pickupEnergy`, …). Story hooks set world state. Some start story events (e.g. `genpet.c` skips time with `MoveGameClockDays`) |
| Orbit and scanning | `src/uqm/planets/scan.c`, `planets.c`, `report.c` (planet report text) | Logic | Adapt | Per player. Scanned and mined-out nodes are world state, so a second player sees the first one's work |
| Planet surface generation | `src/uqm/planets/surface.c`, `gentopo.c` | Logic | Port as-is | Mineral, bio and energy nodes are seeded per planet from `SysGenRNG`, so server sends seeds |

### Lander and mining

| System | UQM location | Kind | Co-op change | Notes |
| --- | --- | --- | --- | --- |
| Lander | `src/uqm/planets/lander.c` (`PlanetSide`, `DoPlanetSide`, `FillLanderHold`, `pickupMineralNode`, `pickupBioNode`, `shotCreature`, `AddGroundDisaster`, `AddLightning`, `KillLanderCrewSeq`, `GetHazardChance`) | Logic | Adapt | Escorts get their own landers. Players on the same planet share one landing screen: each has their own lander, and they see each other. Node pickup is checked on the server so each node is taken once |
| Lifeforms | `src/uqm/planets/lander.c` (`CreatureData`), `lifeform.h` | Data | Port as-is | Extracted to `data/lifeforms.json` |
| Hazards | `src/uqm/planets/lander.c` (quakes, lightning, hot spots), `plandata.c` (tectonics, weather, temperature) | Both | Port as-is | |
| Cargo and bio data | `src/uqm/planets/cargo.c`, `sis.h` (`ElementAmounts`, `TotalElementMass`, `TotalBioMass`, `STORAGE_BAY_CAPACITY 500`, `BIO_CREDIT_VALUE 2`) | Both | Adapt | Per player. Players in the same solar system can trade cargo, limited by the receiver's free storage |

### Starbase and shipyard

| System | UQM location | Kind | Co-op change | Notes |
| --- | --- | --- | --- | --- |
| Starbase visit | `src/uqm/starbase.c` (`VisitStarBase`, `DoStarBase`, `DoTimePassage`, `InstallBombAtEarth`), `comm/starbas/`, `comm/comandr/` | Both | Adapt | `DoTimePassage` skips 14 days (bomb install): a story event that pauses everyone. Commander conversation is story |
| Outfitting (modules, fuel, landers, naming) | `src/uqm/outfit.c` (`DoInstallModule`, `ChangeFuelQuantity`, `DisplayLanders`), `sis.h` (13 purchasable modules, `NUM_MODULE_SLOTS 16`, `NUM_DRIVE_SLOTS 11`, `NUM_JET_SLOTS 8`, `MAX_LANDERS 10`, tank capacities), `GAME_STATE.ModuleCost[]`, `FuelCost` | Both | Redesign | Flagship keeps the full set. Escorts get a smaller module list (open item). Prices into data files |
| Shipyard (crew, escort ships) | `src/uqm/shipyard.c` (`DoShipyard`, `DoModifyShips`, `CrewTransaction`, `DMS_AddEscortShip`, `DMS_ScrapEscortShip`), `GAME_STATE.CrewCost`, `CREW_EXPENSE_THRESHOLD` | Logic | Adapt | Host's AI fleet stays as is. Guest players hire their own crew from their own wallet |
| Flagship state | `src/uqm/sis.h` (`SIS_STATE`: position, `ResUnits`, `FuelOnBoard`, `CrewEnlisted`, module, drive and jet slots, `NumLanders`, `ElementAmounts`, names), `sis.c` | Data | Redesign | Becomes the template for player ship state. `CrewEnlisted = ~0` means game over: in Crateris only for the flagship |

### Conversations

| System | UQM location | Kind | Co-op change | Notes |
| --- | --- | --- | --- | --- |
| Conversation engine | `src/uqm/comm.c` (`InitCommunication`, `HailAlien`, `DoCommunication`, `DoResponsePhrase`, `PlayerResponseInput`, conversation summary, replay), `commglue.c` (`NPCPhrase`, `Response`, `construct_response`, `init_race` dispatch), `commanim.c` (alien animations), `comm.h` (`LOCDATA`) | Logic | Redesign | Server runs the tree (branches, conditions, effects). Client shows lines, animation and voice. Add the dialogue lock (one player per race) and the story pause (everyone watches, the starter chooses). The clock keeps running at its constant rate during ordinary talk |
| Dialogue lines | Content: `comm.<race>.dialogue` (`CONVERSATION:` `.txt` files: `#(PHRASE_ID)  voice.ogg` then the text), `comm.<race>.graphics`, `.music`, `.font`, `.colortable` | Data | Port as-is | Client-only content. The scripts reference lines by id (e.g. `SORRY_ABOUT_THAT`): keep the ids in the ported script. Some lines carry numbers built at runtime (`NPCNumber`, e.g. coordinates) |
| Melnorme trading | `src/uqm/comm/melnorm/melnorm.c` ("Melnorme Sales System": fuel, technologies, info, `TECHPRICE`, `rescue_lines`) | Both | Adapt | Melnorme credits (from bio data) are a separate currency, stored in UQM as game state (`MELNORME_CREDIT0`/`1`). Crateris: per player, like resource units, and transferable between players at any time, including during a conversation. The out-of-fuel rescue must work for each player. Tech purchases change world state |
| Hail and encounter start | `src/uqm/comm.c` (`HailAlien`, `RaceCommunication`), `encount.c` | Logic | Adapt | Who starts the talk owns it. Attack/Converse choice is the starter's |

Per-race conversation scripts. All are **Both** (tree is code, lines are content) and **Adapt**: port the tree to C#, route each `SET_GAME_STATE` to world state, then decide which branches are story (pause for all), which is an open item. Size and state-write counts help estimate the work and spot the story-heavy ones.

| Script dir (`src/uqm/comm/`) | Conversation id | Content id | Lines of C | `SET_GAME_STATE` calls | Notes |
| --- | --- | --- | --- | --- | --- |
| `arilou` | `ARILOU_CONVERSATION` | `comm.arilou` | 855 | 40 | QuasiSpace, portal spawner |
| `blackur` | `BLACKURQ_CONVERSATION` | `comm.kohrah` | 567 | 13 | Also the "surrender" game-over talk |
| `chmmr` | `CHMMR_CONVERSATION` | `comm.chmmr` | 641 | 14 | Flagship refit: story. Also the default case in `init_race` (`commglue.c`) |
| `comandr` | `COMMANDER_CONVERSATION` (before starbase is available) | `comm.commander` | 694 | 19 | Opening of the game: story |
| `starbas` | `COMMANDER_CONVERSATION` (after) | `comm.starbase` | 1961 | 43 | Largest script; mix of story and ordinary talk |
| `druuge` | `DRUUGE_CONVERSATION` | `comm.druuge` | 926 | 37 | Trades ships and crew: wallet and fleet effects |
| `ilwrath` | `ILWRATH_CONVERSATION` | `comm.ilwrath` | 649 | 7 | |
| `melnorm` | `MELNORME_CONVERSATION` | `comm.melnorme` | 1855 | 50 | Trading, see above |
| `mycon` | `MYCON_CONVERSATION` | `comm.mycon` | 643 | 11 | |
| `orz` | `ORZ_CONVERSATION` | `comm.orz` | 898 | 32 | |
| `pkunk` | `PKUNK_CONVERSATION` | `comm.pkunk` | 1148 | 41 | |
| `rebel` | `YEHAT_REBEL_CONVERSATION` | `comm.yehat.rebel` | 449 | 6 | |
| `shofixt` | `SHOFIXTI_CONVERSATION` | `comm.shofixti` | 652 | 20 | |
| `slyland` | `SLYLANDRO_CONVERSATION` | `comm.probe` | 518 | 7 | Slylandro probe |
| `slyhome` | `SLYLANDRO_HOME_CONVERSATION` | `comm.slylandro` | 921 | 38 | |
| `spathi` | `SPATHI_CONVERSATION` (away from home) | `comm.spathi` | 834 | 14 | |
| `spahome` | `SPATHI_CONVERSATION` (at Spathiwa) | `comm.safeones` | 1018 | 38 | |
| `supox` | `SUPOX_CONVERSATION` | `comm.supox` | 708 | 30 | |
| `syreen` | `SYREEN_CONVERSATION` | `comm.syreen` | 878 | 25 | |
| `talkpet` | `TALKING_PET_CONVERSATION` | `comm.talkingpet` | 841 | 22 | |
| `thradd` | `THRADD_CONVERSATION` | `comm.thraddash` | 954 | 44 | |
| `umgah` | `UMGAH_CONVERSATION` | `comm.umgah` | 729 | 13 | |
| `urquan` | `URQUAN_CONVERSATION` | `comm.urquan` | 555 | 11 | |
| `utwig` | `UTWIG_CONVERSATION` | `comm.utwig` | 996 | 55 | Most state writes |
| `vux` | `VUX_CONVERSATION` | `comm.vux` | 796 | 25 | |
| `yehat` | `YEHAT_CONVERSATION` | `comm.yehat` | 685 | 27 | |
| `zoqfot` | `ZOQFOTPIK_CONVERSATION` | `comm.zoqfotpik` | 975 | 11 | |

### Story events and the clock

| System | UQM location | Kind | Co-op change | Notes |
| --- | --- | --- | --- | --- |
| Game clock | `src/uqm/clock.c` (`GameClockTick`, `MoveGameClockDays`, `AddEvent`, `processClockDayEvents`), `clock.h` (`HYPERSPACE_CLOCK_RATE 5`, `INTERPLANETARY_CLOCK_RATE 30` seconds per day) | Logic | Redesign | Vanilla ticks only in hyperspace (`starcon.c`) and interplanetary flight (`solarsys.c`); it stops in battles, conversations, orbit, on planets and at the starbase. Crateris: one shared clock at a constant rate (host setting), whatever players are doing; only story pauses and the host's pause stop it. Keep UQM's calendar, day processing and event scheduling (`AddEvent`, `processClockDayEvents`). Logged in `docs/differences.md` |
| Story events | `src/uqm/gameev.c`, `gameev.h` (18 events: Arilou entrance and exit, hyperspace encounter, Kohr-Ah victorious, Pkunk, Thraddash, Utwig/Supox, Ilwrath and Mycon missions, Zoq-Fot distress and death, Shofixti return, Kohr-Ah genocide, Spathi shield, Arilou/Umgah check, Yehat rebels, Slylandro ramp up and down) | Both | Adapt | World state. Event table to data, handlers to C#. Events that show something to the player become story pauses |
| Time skips | `MoveGameClockDays` in `starbase.c` (`DoTimePassage`, 14 days) and `planets/generate/genpet.c` | Logic | Adapt | Shared clock: a skip moves time for everyone, so it must be a story pause |
| Key story items (devices) | `src/uqm/globdata.h` (`*_DEVICE` states: portal spawner, Talking Pet, Utwig bomb, Sun device, Rosy Sphere, Aqua Helix, Clear Spindle, Ultron 0–3, Shofixti maidens, Umgah and Burvixese hyperwave casters, data plates 1–3, Taalo protector, egg casings 0–2, Syreen shuttle, VUX beast, destruct code, Ur-Quan warp, Lunar base and others), `planets/devices.c` (using devices) | Both | Redesign | Group inventory, as in the design. Using a device (e.g. the portal spawner, Sun device) needs a rule for who may use it and where |
| Endgame | `src/uqm/ships/lastbat/`, `planets/generate/gensam.c`, `starcon.c` (`IN_LAST_BATTLE`, `WON_LAST_BATTLE`, Kohr-Ah surrender ending), `credits.c` | Both | Adapt | Final battle with the whole group. Game over and victory screens for all players |

### Save, load and UI

| System | UQM location | Kind | Co-op change | Notes |
| --- | --- | --- | --- | --- |
| Save | `src/uqm/save.c` (`SaveGame`, `SaveGameState`, `SaveSisState`, `SaveShipQueue`, `SaveRaceQueue`, `SaveGroupQueue`, `SaveEncounters`, `SaveEvents`, `SaveClockState`, `SaveStarInfo`, `SaveGroups`, `PrepareSummary`) | Logic | Redesign | Host save holds world state plus every player's state by stable player id, including absent players. New readable format (e.g. JSON). Importing a solo UQM 0.8.0 save: only if cheap; see open question 9 |
| Load | `src/uqm/load.c`, `load_legacy.c` | Logic | Redesign | Same as save. `load_legacy.c` (pre-0.8 saves) not needed |
| Game menu and setup | `src/uqm/gameopt.c` (save, load, quit), `setupmenu.c`, `settings.c`, `setup.c`, `menu.c`, `confirm.c` | Logic | Redesign | Godot UI. Add host, join, leave, player list, settings for every open number |
| HUD and status panel | `src/uqm/status.c`, `sis.c` (SIS frame, fuel, crew, coordinates via `DrawHyperCoords`), `flash.c`, `planets/roster.c`, `cargo.c` | Logic | Adapt | Godot UI showing the player's own ship. Add other players' status |
| Cutscenes and intro | `src/uqm/intro.c`, `fmv.c`, `credits.c`, `demo.c`, content `slides.*`, 3DO video add-on | Both | Adapt | Client-only. Intro on host start. Story cutscenes are shown to all (story pause) |
| Pause | `src/uqm/gameinp.c`, `CHECK_PAUSE` activity flag | Logic | Redesign | Only the host can pause; pause freezes everyone, including combat |

## Open questions found while surveying

These are new; the ones already in `docs/design.md` are not repeated.

1. ~~Clock rate across activities~~ Decided 2026-10-31: one constant rate whatever players are doing. The rate itself is an open item in `docs/design.md`.
2. ~~Clock in orbit, on planets and at the starbase~~ Decided with 1: it runs there too.
3. ~~Two players on the same planet~~ Decided: one shared landing screen.
4. ~~Battle rewards~~ Decided: fungible rewards are split equally. In UQM the only battle salvage is resource units (see the battle start row); crew and items don't drop.
5. ~~Targeting among several player ships~~ Decided: nearest opponent, which is what UQM's `TrackShip` already does.
6. ~~Melnorme credits~~ Decided: per player like resource units, transferable at any time, including during conversations.
7. Device use: who can use a group item, such as the portal spawner or the Sun device, and does it need the whole group present? **Deferred** until the impact is clearer.
8. ~~`.mod` music~~ Decided: any tracker player that works; pick when building audio.
9. Import a solo UQM 0.8.0 save as a new host save: **only if cheap**. Estimate: UQM's `load.c` is about 770 lines reading a binary format (summary, game state bits, flagship, ship and race queues, encounters, events, clock, per-star state). Because Crateris ports the same game state table, most of it maps directly; the solo flagship becomes the host's. Revisit once the Crateris save format exists.
