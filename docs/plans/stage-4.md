# Stage 4 plan: combat

**Status:** draft, 2026-11-10, for the owner's review: fifteen decisions wait on them (see "Decisions", recommendations first).

Build order item: "4. Combat, including joining battles, one enemy at a time, and the minimap" (`docs/design.md`), after stage 3a and before 3b, since most races' conversations can end in a battle (stage 3, decision 2). Stage 3a left these to stage 4: the Ilwrath fight at Earth (`docs/differences.md` row 39, stage 3 decision 3), the battle's own fields (row 36, stage 3 decision 11), the escort fleet's ships at the shipyard (row 44), the Arilou's gift of ships (row 43) and the Sa-Matra (row 27). The content stage left the ship and battle graphics to it (`docs/plans/content-stage.md`).

A few terms used below:
- **An element** is anything in UQM's battle: a ship, a shot, an asteroid, crew floating in space, an explosion. Each has four callbacks (what it does before moving, after moving, on a collision and on dying) and a place in one list, whose order decides which collisions happen first (`element.h`, `process.c`).
- **A side** is UQM's `playerNr`: the player's (`RPG_PLAYER_NUM`, 0) or the enemy's (`NPC_PLAYER_NUM`, 1). UQM's battle has two sides with one ship each at a time.
- **The arena** is the battle's space: a torus 8,192 by 7,680 world units in UQM (`LOG_SPACE_WIDTH`, `LOG_SPACE_HEIGHT`), with one planet and five asteroids. **The seam** is where it wraps round.
- **A trace** is UQM's own battle code run by a recording program with the drawing stubbed, its state written each frame, which the port must match.

## Splitting stage 4

UQM's combat is about 8,600 lines of engine (the simulation 5,200, the AI 1,400, the battle screen 2,000) and about 13,900 lines of ships, 28 files from 118 lines (the Ur-Quan drone) to 1,083 (the Orz). Before stage 3b nothing in space can start a battle but the Ilwrath at Earth, so most ships can't be met until then. This plan splits stage 4 in two (decision 1) and plans the first part in detail:

| Part | What | Needs |
| --- | --- | --- |
| **4a. The battle, together** (this plan) | The battle engine checked against UQM's traces; the flagship and escorts in combat (`sis_ship.c`, with each ship's own modules); the Ilwrath ship and its AI; several players against one enemy: battle markers, joining, the bigger arena, each player's camera, status and minimap, retreat, disconnects, wrecks and revival, salvage, game over; the Ilwrath fight in the opening | 3a |
| 4b. Every race's ship and the host's fleet | The other 24 race ships with their AI (about 11,500 lines), each checked against UQM's traces, among them the ones whose code assumes one opponent (Orz, Melnorme, Shofixti, VUX, Pkunk) and the Syreen, whose song is the only thing that sets crew floating; the host's AI fleet (`built_ship_q`): the shipyard building, scrapping and crewing fleet ships, the Arilou's gift, the host sending one fleet ship into battle instead of the flagship and choosing the next when one dies or retreats; the enemy's fleets of several ships, one at a time | 4a |

The Sa-Matra (`lastbat.c`, 926 lines) and the final battle go with the endgame, after 3c: nothing reaches it before then, and its gate, which wins the battle for whichever player's ship touches it carrying the bomb, needs the bomb (3c) and a co-op rule of its own.

## Goal of stage 4a

The group fights together:

1. **The Ilwrath at Earth.** In the opening, the Ilwrath ship's talk ends in a battle, as in UQM. The player who talked fights it in UQM's battle: UQM's arena, planet and asteroids, the Ilwrath's hellfire and its AI (at Earth UQM's Ilwrath, 7 crew strong, doesn't cloak), the flagship's or the escort's own guns. A marker shows the battle by Earth, and the others fly into it to help.
2. **Fighting together.** Each player sees the battle from their own ship, zooming as UQM's camera does between them and the enemy, with UQM's status panel for their ship and the enemy's, and a minimap of the arena with every ship.
3. **Losing a ship.** A guest's escort that dies becomes a wreck; a friend revives it by flying to it and giving it crew. The flagship's death ends the game for everyone.
4. **Winning and retreating.** The enemy destroyed, the salvage is split between the players who fought to the end (the Earth Ilwrath pays none, as in UQM). After the starbase is available, a player can retreat as in UQM, for five units of their own fuel.

### What the owner will be able to try when it's done

- Start a game with `--start-visit report` (new, testing: the opening's visit at the talk where the Lunar base is reported, with the errands done) and reply to the Ilwrath. The battle starts on your screen; the others see its marker by Earth. Fly the flagship, fire its gun, dodge the hellfire. Win, and the commander's alliance talk follows.
- As a guest, fly into the marker while the host fights: your escort warps in, and both screens follow their own ship. Your shots don't hit the host.
- Lose the escort: it becomes a wreck by Earth once the battle is over; the host flies to it and gives it crew, and you fly on.
- Lose the flagship: every player sees the game over.
- `--start-battle ilwrath` (testing) starts a battle against any ported ship by the host's ship, for others to fly into; there the Ilwrath cloaks, as it does everywhere but at Earth. With `--start-docked`, where the starbase is available, Escape retreats. `--script` takes `fire`, `special` and `escape`.
- Save, load, quit mid-battle: each player is back where the battle started, with what their ship had then.

## Scope

**In stage 4a:**

- **The battle engine:** elements, the list and its order, movement, collisions and their momentum (`process.c`, `collide.c`, `velocity.c`, `displist.c`), gravity and the planet (`gravity.c`), asteroids, explosions and floating crew (`misc.c`), weapons and homing (`weapon.c`), ships' turning, thrust, energy and firing (`ship.c`), dying, warping in and out (`tactrans.c`), and the battle's random numbers, all checked against UQM's traces of one ship against one.
- **Ships:** the flagship (`sis_ship.c`), built from a ship's own modules, so escorts fight with theirs (decision 3), and the Ilwrath (`ilwrath.c`).
- **The AI:** `cyborg.c` and `intel.c`, and the Ilwrath's `ilwrath_intelligence`, choosing among several player ships.
- **Co-op battles:** markers in systems and hyperspace, joining, the arena's size and seam, friendly fire, each player's camera, status and minimap, retreat, disconnects, story pauses, wrecks and revival, salvage, the battle's end, game over (decisions 2 to 14).
- **The content:** the battle graphics (each ship's hull, weapon and special sheets at three sizes, captain windows, explosions, asteroids, the stars, the planets' battle sprites, the status panel's frames), and the collision masks from them, recorded by UQM's own loader as the lander's were.
- **The escorts' combat modules** (decision 3): their kit's gun, outfitting's combat modules for escorts.
- **The Ilwrath fight in the opening** (row 39 closed).
- **The host's pause** (row 12), since a battle is where it matters most (decision 15).
- **Wire and saves:** a battle activity and screen, the battle sent to each player near their ship, a Special key; saves keep wrecks.

**Left out of 4a:**

| Left out | Why | Goes to |
| --- | --- | --- |
| The other 24 race ships and their AI | Nothing can meet them before 3b; each is a port and a trace of its own | 4b |
| The host's AI fleet: building, scrapping and crewing fleet ships at the shipyard, the Arilou's gift, sending a fleet ship instead of the flagship, picking the next ship (`pickship.c`) | Needs the race ships | 4b (rows 43 and 44 stay until then) |
| The enemy's fleets of several ships, one at a time (`BuildBattle`'s queues, `npc_built_ship_q`) | The Ilwrath comes alone; fleets come with the races' encounters | 4b, met in 3b |
| Crew floating in space, and how it's shared at a battle's end | Only the Syreen's song sets crew floating (`AbandonShip`) | 4b, with the Syreen. The engine's floating crew is ported and traced in 4a |
| Alien ships flying at players in systems and hyperspace (`encount.c`, `ipdisp.c`, `hyper.c`), the choice to talk or fight | Ships in space are 3b | 3b |
| The Sa-Matra and the final battle (`lastbat.c`) | Needs the bomb and the endgame | The endgame, after 3c |
| Battle sounds and music (`sounds.battle`, `music.battle`, each ship's `ditty`) | Nothing plays sound yet (stage 3's left-out table) | A later stage with audio |
| The Cyborg option (the computer flying the player's ship) and Super Melee | Not part of the co-op game | Not planned |

## How UQM does it

### A frame

`Battle` (`battle.c:396`) seeds the one random generator from the clock (`TFB_SeedRandom(GetTimeCounter())`), so no two battles are alike, sets the arena up (`InitShips`, `init.c:178`: the stars, one planet, five asteroids; in the last battle the stars only, round the Sa-Matra), and runs `DoBattle` 24 times a second (`BATTLE_FRAME_RATE`).

Each frame, `ProcessInput` turns each side's keys into its ship's input bits, and `RedrawQueue` (`process.c:1013`) steps the whole list:
1. **Before moving** (`PreProcessQueue`): each element in list order runs its death if its life is up, its own `preprocess_func` (for a ship, `ship_preprocess`: energy, turning, thrust, and on the first frame the warp-in), moves by its velocity, and is checked for collisions against the elements after it (`ProcessCollisions`: the time of impact over the sprites' masks, both collision callbacks, then `collide()`'s momentum exchange by mass).
2. **After moving** (`PostProcessQueue`): elements added this frame are stepped and collided too, the disappearing ones go, each runs its `postprocess_func` (for a ship, `ship_postprocess`: firing, the special), and the frame to draw is chosen for the zoom.

The list holds at most 150 elements (`MAX_DISPLAY_ELEMENTS`); a shot that finds it full isn't fired. Which element is first in the list (`InsertElement` puts it at the head, `PutElement` at the tail) changes which of two collisions happens first.

### The camera and the seam

UQM's coordinates move with the camera: each frame the camera's scroll is added to every element (`PostProcessQueue`), so the view's centre stays the arena's and the seam stays opposite it. The camera is the wrap-aware midpoint of the two ships, zoomed out to keep both in view, from 1× to 4× (`CalcReduction`, `CalcView`; in steps of 1×, 2× and 4× with the step zoom); at 4× it shows half the arena each way, so the seam is never on the screen.

Gravity, homing and the AI measure across the seam (`WRAP_DELTA`), but collisions don't: their tests take display coordinates as they are (`InitIntersectStartPoint`, `collide.h:40`; `intersec.c`), and so does the flagship's point defence's range (`sis_ship.c:452`). Two elements either side of the seam pass through each other. In UQM that never shows, as the seam is off the screen, opposite both ships. Asteroids are put at x 0 or y 0, on the seam, at the start and whenever one is destroyed (`spawn_asteroid`, `misc.c:93` and `:147`); the stars scroll with the camera.

### Sides

Collisions and weapons don't look at sides: a shot hits any element but those of the ship that fired it (`IGNORE_SIMILAR`, same `pParent`; `weapon_collision`), and the flagship's and the Earthling cruiser's point defence fire at any element near them but their own ship (`sis_ship.c:443`, `human.c:202`; only the Chmmr's checks the side). Homing (`TrackShip`, `weapon.c:319`) turns towards the nearest opposing ship by |dx| + |dy| and keeps it once chosen (`hTarget`); the AI chooses its enemy as the nearest ship by straight-line distance, without a side check (`cyborg.c:1138`). With two ships these agree; with several they don't.

### Ships

A ship is its race's description (`RACE_DESC`: crew, energy, thrust, turning, weapon and special costs and waits, mass, the AI's range) and its callbacks. The flagship builds its description from its modules (`init_sis`, `sis_ship.c:919`): its guns from the weapon modules and where they are (`InitWeaponSlots`), its energy from the dynamos and furnaces, its thrust and turning from the drives and jets, its crew from `CrewEnlisted` plus the captain, which `uninit_sis` writes back (`CrewEnlisted = crew_level - 1`: for a ship that died, `~0`, the game over). The Ilwrath fires short-lived hellfire and cloaks, uncloaking when it fires (`ilwrath.c`, 409 lines); its AI doesn't cloak in an encounter while `PROBE_ILWRATH_ENCOUNTER` is set (`ilwrath.c:179`), which is the Earth fight.

The status panel has two slots, the enemy's at the top and the player's below (`status.c`): each ship's icon, name, crew and energy bars, and its captain's window, animated as the ship turns, thrusts and fires. UQM draws them from inside the simulation (`DeltaCrew`, `DeltaEnergy`), and a few ships draw on the other's silhouette (`ModifySilhouette`, which also draws a random number).

### Losing, retreating, winning

- **A ship dies** (`ship_death`, `tactrans.c:730`): it explodes over 36 frames, its side's count goes down (`battle_counter`), and its side picks the next ship: the enemy the next in its queue, the player from a menu that stops the battle while they choose (`GetArmadaStarShip`). Picking the flagship ends the choosing, so it's the player's last ship; when the last ship dies, it's game over (`CrewEnlisted = ~0`, `pickship.c:345`).
- **Retreat** (`RunAwayAllowed`, `DoRunAway`, `battle.c:64`): only in an encounter or the last battle, after the starbase is available, not carrying the bomb, and not while the ship warps in. The ship flashes red and warps out keeping its crew, and can still be hit until it's gone (`flee_preprocess`); it can't be picked again in that battle. When the player's last ship leaves this way, it costs 5 units of fuel, down to no lower than 0 (`RUN_AWAY_FUEL_COST`, `pickship.c:345`).
- **The end** (`UninitShips`, `UninitEncounter`, `encount.c:476`): the crew still floating goes to the surviving ship. On a victory each enemy ship destroyed pays `RACE_SHIP_COST / 8` resource units (an Ilwrath, 1,000, pays 125). Dead escorts are gone, and the enemy's survivors keep the crew they have left (`UpdateShipFragCrew`).
- **After it:** outside hyperspace, the flagship can't collide with anything for 110 frames of flight (`ESCAPE_COUNTER`, counted down in `ipdisp.c:694`), so it can fly clear of the ships it met. `BuildBattle` sets it before any talk with a ship in a system (but the Chmmr's and the Syreen's), battle or not: in 4a that's only the Ilwrath's; 3b's talks set it too.

### Entering a battle

Before a talk with an alien ship, UQM builds the enemy's side, the planet (`BATTLE_PLANET` in a system, a random one in hyperspace) and the escape counter (`BuildBattle(NPC_PLAYER_NUM)`, `comm.c:1372`). A talk that ends hostile (`BATTLE_SEGUE`) builds the player's side (the escorts, then the flagship) and fights (`EncounterBattle`, `comm.c:1430`), as an encounter (`IN_ENCOUNTER`) unless it's the last battle.

The Ilwrath at Earth comes this way from its talk (`VisitStarBase`, `starbase.c:463`): one Ilwrath with 7 crew of its 22, marked so that its battle pays no salvage and shows no tally (`race_id = ~0`). It's an encounter, but the starbase isn't available yet, so it can't be retreated from. Losing it is the flagship's death: game over.

## Co-op rules for stage 4

Decided in `docs/design.md`:
- Battles show as a marker where they started, in a system or hyperspace, to the players there; flying into it joins (row 15). All players in it fight at once (row 5), against one enemy ship at a time, unscaled.
- Enemies target the nearest opponent (`TrackShip`).
- Each player has their own camera and an arena minimap (row 6), in a bigger arena (row 7).
- A player's ship that dies becomes a wreck at the death location; a rescuer revives it by giving it crew, keeping at least 1 (row 8). The flagship's death ends the game for the group.
- Retreat as in UQM, per player: when a player's last ship leaves (in 4a, their only ship), they lose 5 units of fuel. A disconnect mid-battle is a retreat with the same fuel cost, even where UQM forbids it, and never a loss (row 4).
- Salvage split equally between the players in the battle when it's won; retreated and disconnected players get none (row 18).
- Story talks pause the game for everyone (row 2); only the host pauses, and the pause freezes combat (row 12).
- A new guest whose host is in a battle starts at the host's position in the surrounding system or hyperspace (row 14): by the battle's marker, outside it.
- The battle runs on the host at 24 frames a second; clients send keys with the fixed input delay and draw what they're sent, near their own ship.

## Work, in order

Each item ends with tests that run headless, in a new topic, `combat` (`tests/topics.json`). The engine is checked as the lander was: UQM's own battle code runs in a recording program with the drawing, sound and input stubbed, the seed fixed and the keys scripted, and the port must match it frame by frame.

1. **The battle's data and content.**
   - The ships' descriptions from `data/ships.json` (already extracted, read by nothing yet).
   - The battle graphics by their resource ids (`ship.<race>.graphics.*` at three sizes, `graphics.boom.*`, `graphics.blast.*`, `graphics.asteroid.*`, `graphics.stars`, `graphics.status`, the planets' battle sprites), and their collision masks, recorded by UQM's own image loader (`write_masks`, as the lander's were).
   - Check: every resource the flagship, the Ilwrath and the arena use loads, and the masks match UQM's.
2. **The engine, two ships.** `Combat/`: elements, the list, before and after moving, collisions with their time of impact, `collide()`, gravity, the planet, asteroids, explosions, floating crew, weapons and homing, a ship's energy, turning, thrust and firing, dying and warping in, with UQM's random generator per battle. In UQM's arena with UQM's camera, so a battle of one ship against one is UQM's.
   - Check: traces from UQM's own `process.c`, `collide.c`, `gravity.c`, `misc.c`, `weapon.c`, `ship.c`, `tactrans.c` and `init.c` (a new recording program, `tools/extract/golden/battle.c`, `tests/golden/battle.json`): scripted keys for both ships over hundreds of frames each, seeded; each frame every element's state as Super Melee's netplay checksum takes it (flags, life, crew, mass, turn and thrust waits, velocity, place), the camera's scroll, and each random draw's place. Scenarios: flying into the planet, ramming, shooting, the list full, asteroids destroyed and put back, crew floating (`AbandonShip`, called directly), a ship dying.
3. **The flagship and the Ilwrath.** `sis_ship.c` ported to build a ship's description from that ship's own modules and crew, not the one flagship's, with no game-over sentinel for an escort (decision 3); `ilwrath.c`.
   - Check: traces of the flagship with several module layouts (guns forward and aft, blasters, cannons, tracking, point defence, dynamos and furnaces) against the Ilwrath, and of the Ilwrath's hellfire and cloak (outside the Earth fight).
4. **The AI.** `intel.c`, `cyborg.c` and `ilwrath_intelligence`; the enemy chooses among the player ships by UQM's own rule (the nearest), and only opposing ships.
   - Check: traces of the Ilwrath flown by UQM's AI against a scripted flagship, at Earth (`PROBE_ILWRATH_ENCOUNTER` set: no cloak) and elsewhere; tests of our own for its choice among several.
5. **Several ships.** The sides become the players' and the enemy's, with any number of player ships at once: no friendly fire, point defence included (decision 2); the arena's size from the setting (decision 4); collisions and point defence measured across the seam (decision 5). Each player ship warps in where UQM's `spawn_ship` puts one; homing and the AI choose among them.
   - Check: tests of our own: four player ships against the Ilwrath, shots and point defence passing teammates, the AI changing target, joining mid-battle, a collision across the seam, the arena's size; UQM's traces still matched with one player and the arena at UQM's size.
6. **Battles in the world.** A battle activity for each ship in it; a battle marker where it started, in hyperspace or the part of a system (outer or inner) it started in, shown to the players there, gone when it ends; flying into it joins, warping in (decision 6); retreat as UQM allows it, per player, with the fuel cost (Escape, the bit taking off uses); a disconnect as a retreat at once (decision 10); story pauses freezing battles (decision 11); the battle's end and its salvage, the remainder as decision 9 says, none for a battle UQM marks so (the Earth Ilwrath); the battle's own fields, each battle's (`BATTLE_PLANET`) or each ship's after it (`ESCAPE_COUNTER`: 110 frames unable to collide in system flight), as stage 3's decision 11 kept them aside; each player's ship as it was when they joined the battle, for a load (decision 14); the clock runs on (row 1).
   - Check: `GameWorld` tests: starting, joining, retreating, disconnecting, winning and losing with one to four players, salvage and its remainder, a story pause mid-battle, a battle no one is left in, the escape counter.
7. **Wrecks, revival and game over.** A player ship that dies becomes a wreck at the battle's place once it's over (decision 7): it doesn't fly, land or talk, and its player watches the battle, then their wreck. A rescuer flies into it and gives it crew (decision 8). The flagship's death ends the game for the group (decision 12). Saves keep wrecks, among them ships destroyed in a battle still running (save version 10).
   - Check: tests of the wreck's place, revival keeping the rescuer at least 1, a wreck's player leaving and rejoining, the wreck saved and loaded, the flagship's death.
8. **The escorts' combat modules.** The escort's kit gets a gun (a setting), and escorts already saved without a weapon are given it on load, in their first free module slot (none if every slot is full); outfitting sells escorts the combat modules (decision 3), in their kit's slots.
   - Check: `OutfittingTests` for an escort's combat modules and the flagship's unique ones refused; a save of version 9 loaded.
9. **The Ilwrath fight in the opening.** Its talk's hostile ending starts a battle by Earth for the talk's starter, against UQM's Ilwrath of 7 crew, with no salvage; the opening goes on once it's won (decision 13).
   - `--start-visit report` (testing) starts the opening's visit at the report talk, with the errands done.
   - Check: `OpeningTests`: the battle starts; won by the starter or another player leads to the alliance talk; the starter wrecked passes the replies on; lost by the flagship ends the game; every player out of it takes the visit back; row 39 closed.
10. **The host's pause.** A key for the host only that stops the world, the battles in it and the clock, shown to every player (decision 15).
11. **The wire.** The battle activity and screen; a Special key (`ShipInput` has two free bits; the guest's input mask, now 63, takes it); each player sent the elements near their camera, their own ship's and the enemy's status, and the minimap's dots (a new codec, protocol version 20), and reliable messages for a battle's start, end and wreck.
    - Check: `SnapshotCodecTests` round trips and refusals; the network test with a guest joining a battle over the lossy relay, measuring what it sends.
12. **Client views.** `Presentation/BattleScreen.cs`: UQM's battle drawn as UQM draws it from what the host sends, at each player's camera, as the paragraph after the decisions describes: the stars' three planes, the planet, each element's frame at the zoom's size, explosions, the ion trails; the status panel's two slots, the enemy's and the player's own, with the captain's window, crew and energy; the minimap. Battle markers and wrecks in the system and hyperspace views; the rescue screen; the game over screen.
    - Check: `BattleScreenTests` against UQM's own drawing (the status panel and a frame's sprites, recorded as the starbase's screens were); screenshots.
13. **Docs.** `docs/how-to-try/stage-4a.md`; `design.md` (the escort module set's combat part, the build order); `porting-inventory.md` (the combat rows); `differences.md`: rows 1 and 12 (the pause), 2 (story pauses freeze battles), 4 to 8, 9 and 46 (escorts' combat modules), 13 (save version 10), 14, 15, 16, 18, 27 (the Sa-Matra now with the endgame), 36 (the battle's fields), 39 (closed), 43 and 44 (waiting for 4b), and new rows for what 4a decides (friendly fire, the seam, the arena's frame, wrecks' place, unsaved battles); `uqm-data.md`.

## Decisions

Recommendations first.

1. **Splitting stage 4.**
   - **Two parts, 4a and 4b, as above; the Sa-Matra with the endgame (recommended).** 4a can be played through (the Ilwrath fight, together), and 4b's ships each plug into an engine already checked.
   - One stage 4: every ship before anything can be played; about twice 4a's size.
2. **Friendly fire.** UQM's shots hit any ship but the one that fired them, point defence fires at any element near it, and ships bump each other: with several players, they'd shoot each other.
   - **None between players (recommended):** players' shots and specials pass through teammates and their shots, point defence ignores them, and teammates' ships don't collide. Area effects (the Shofixti's self-destruct, in 4b) spare teammates.
   - UQM's rules as they are: teammates hit and bump each other, and a flagship's point defence spends its energy on a teammate's shots.
3. **Escorts in battle.** This sets the combat part of the escort module set (an open item in `design.md`); the rest stays open.
   - **The flagship's battle code with the escort's own modules and crew; escorts may install the flagship's combat modules (recommended).** `sis_ship.c` builds any player's ship from its modules, so an escort with one gun fights like a flagship with one gun. Escorts start with a gun (the escort's kit, a setting; saved escorts without a weapon get it), and at the starbase may buy the gun, blaster, cannon, tracking, point defence, dynamo and furnace, within their kit's slots; the flagship keeps the bomb's parts and its other unique upgrades.
   - Escorts fly a race ship of their own (the Earthling cruiser, say): simpler to balance, but unlike the escort that lands and mines, and it brings that ship's port and traces from 4b into 4a.
4. **The arena's size** (row 7's "size to set").
   - **UQM's arena times a setting, 1.5 each way by default (recommended).** Four ships have room. Each player's camera zooms as UQM's does, out to UQM's 4×, which shows a third of this arena each way: an enemy farther off than that is off the screen, where UQM's never is, and the minimap shows it. Solo battles are a little roomier than UQM's; the traces run at a setting of 1.
   - UQM's size: solo battles exactly as UQM, but four ships crowd.
   - Growing with the players in it: a torus can't grow without moving everything in it.
5. **The seam and the frame.** UQM's collisions don't cross the seam, which its one camera keeps off the screen; with several cameras, players could fight on it, their shots passing through ships.
   - **Collisions and point defence measured across the seam, as gravity and homing are, in any arena bigger than UQM's; UQM's moving frame kept, following the camera of the battle's first player still in it (recommended).** Nothing passes through anything where players can see it. In UQM's own arena (a setting of 1, which the traces use) UQM's rule holds: asteroids come back on the seam and can sit either side of it, and measured across it they'd bounce where UQM's pass through, changing the random numbers after. The asteroids still come back on the seam, opposite the first player: another player near it sees one appear from nothing, as UQM's player never does.
   - UQM's collisions as they are: players near the seam would see shots pass through ships.
   - Fixed coordinates, the traces converted by their scroll: the asteroids would come back on a seam of our own, and the traces wouldn't be matched there.
6. **Joining a battle.**
   - **Any player whose ship flies into the marker, in hyperspace or the same part of the same system (the inner system, for the Ilwrath by Earth), not in a talk or wrecked, and who hasn't retreated from it (recommended).** They warp in where UQM's `spawn_ship` would put a ship, as UQM's ships warp in. A player whose ship is in it leaves only by retreat (where UQM allows it), disconnecting or death.
   - Only players there when it starts: nobody could fly in to help.
7. **Where the wreck goes and what its player does.** `design.md` says "a wreck at the death location"; this reads it as the battle's place in the world, since the arena goes when the battle ends.
   - **At the battle's place in the system or hyperspace once the battle is over; its player watches the battle on, then their wreck (recommended).** The wreck doesn't fly, land or talk; it keeps its cargo, fuel and modules; it's saved. Mid-battle the dead ship explodes as UQM's do.
   - The wreck stays in the arena and can be revived mid-battle: closer to the words, but a battle's arena would have to outlast its battle.
8. **Revival.**
   - **A rescuer flies into the wreck and chooses how much crew to give it on a screen like the giving screen, keeping at least 1; the wreck flies on with that crew (recommended).** Any player not in a battle or a talk, whose ship isn't a wreck and has more than 1 crew, can rescue.
   - Flying into the wreck gives it half the rescuer's crew: no screen, but no choice.
9. **A battle's end and its salvage** (row 18's "rounding of remainders to set").
   - **Won when the enemy dies with a player ship still in it; lost when no player ship is (recommended).** On a win the salvage is split equally between the player ships still in it at the end (`design.md`'s "players in the battle when it's won", read as those whose ships are still fighting), the remainder to the one that joined first, as stage 3's decision 5 split the starbase's gift. A battle UQM marks as paying nothing (the Earth Ilwrath) pays nothing. A lost battle leaves the enemy where it is with the crew it has left (3b's encounters); the Ilwrath comes again whole (decision 13).
   - The salvage split between every player who was in it, the dead and the wrecked too: kinder, but not what the design says.
10. **A disconnect mid-battle.** `design.md`: a retreat, "never a loss".
    - **The ship leaves at once, without UQM's red flashes and warp-out, keeping its crew and paying the fuel (recommended):** UQM's fleeing ship can still be hit while it flashes, which could make a disconnect a loss.
    - UQM's flee and warp-out: as a retreat looks, but a dropped connection could die on the way out.
11. **Story pauses during a battle.** A story talk pauses the game for everyone (row 2); before 3b, a battle from `--start-battle` can be running when the Ilwrath's talk starts.
    - **A story pause freezes every battle, as it freezes flight; the players in them watch the talk, and fight on after (recommended).** A player in a battle can't start a story talk.
    - Battles run on through the pause: those fighting would miss the story.
12. **Game over.**
    - **Every player sees UQM's game over; the host's game then ends, and loading the save goes back to before the battle (recommended).** The host's save is kept as it was, so the group can try again.
    - The game goes on without the flagship: against UQM's story, which is the flagship's.
13. **The Ilwrath fight.**
    - **The talk's starter fights it; the others, freed from the story pause, may fly in; the opening goes on once it's won, by whoever wins it (recommended).** Losing the flagship is game over, as in UQM. If the starter's escort is wrecked, the alliance talk's replies pass to the next player in join order, as when a starter leaves (stage 3, decision 7), and that player's ship docks at the menu when the opening ends; the wreck stays by Earth. If every player leaves the fight unwon (wrecked, or disconnected, which retreats), the visit is taken back, as a visit is when its starter leaves with nobody to pass it to: the starter's ship is put back as it was before the visit, as now (its fuel too, so a disconnect costs them nothing), but a wreck stays a wreck; the Ilwrath comes again on the next report to the commander. No retreat, as in UQM (the starbase isn't available yet).
    - The flagship always fights it, wherever the starter is: UQM's opening is the flagship's, but a guest could start a fight they're not in.
14. **Saving during a battle.**
    - **Battles aren't saved: on load, a player who was in one is back where it started, outside it, with what their ship had when they joined it (recommended),** as talks and visits aren't saved now. A ship destroyed before the save, in a battle over or still running, is saved as a wreck at the battle's place; the Ilwrath's talk, being part of the visit, comes again.
    - Save the battle, every element: a big new save format for little gain.
15. **The host's pause** (row 12) in 4a.
    - **In 4a (recommended):** a key for the host stops the world, every battle and the clock, and every player sees that it's paused. Battles are where pausing matters, and UQM's pause stops one.
    - Later: pausing waits, and a host can't stop a battle to answer the door.

What each player sees is set by `design.md` (their own camera and a minimap); the plan draws it this way, which the owner can change without a decision: UQM's battle screen at each player's own camera, framed between their ship and the enemy as UQM frames two ships; the status panel's two slots, the enemy's and their own; a minimap of the whole arena with every ship, the planet and the wrecks, in a corner of the space screen, teammates' crew and names by their dots.

## Risks

- **Trace parity.** One random generator feeds the simulation, the AI, the stars, explosions and the status panel's silhouettes (`ModifySilhouette`); the port must draw the same numbers in the same order, including in code that draws. The recording program records each draw's place, as the talks' did.
- **Drawing mixed into the simulation.** UQM draws the status panel and the captain's window from inside the step (`DeltaCrew`, `DeltaEnergy`, `ship_preprocess`). The port keeps the state and draws it on the client, and the traces record the calls separately.
- **Collision order.** Which collision happens first depends on the list's order and its 150-element cap; the port keeps both, and with four players the cap is reached sooner. The cap is UQM's for one player; with more it's a setting, checked to hold the traces at UQM's.
- **Bandwidth.** At UQM's cap of 150 elements, a player sent every element near their camera as deltas gets up to about 2 kilobytes a frame, 48 kilobytes a second: about 1.2 megabits a second of the host's upload for three guests, more if the cap is raised. The network test measures it over the lossy relay; if it's too much for a home connection, fewer elements are sent to players far from them.
- **Size.** 4a ports about 8,600 lines of engine and 400 of ships, plus `sis_ship.c`'s 1,000; 4b's 24 ships are about 11,500 more, mostly mechanical once the engine is checked.
