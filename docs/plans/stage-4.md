# Stage 4 plan: combat

**Status:** draft, 2026-11-10, for the owner's review: fourteen decisions wait on them (see "Decisions", recommendations first).

Build order item: "4. Combat, including joining battles, one enemy at a time, and the minimap" (`docs/design.md`), after stage 3a and before 3b, since most races' conversations can end in a battle (stage 3, decision 2). Stage 3a left these to stage 4: the Ilwrath fight at Earth (`docs/differences.md` row 39, stage 3 decision 3), the escort fleet's ships at the shipyard (row 44), the Arilou's gift of ships (row 43) and the Sa-Matra (row 27). The content stage left the ship and battle graphics to it (`docs/plans/content-stage.md`).

A few terms used below:
- **An element** is anything in UQM's battle: a ship, a shot, an asteroid, crew floating in space, an explosion. Each has four callbacks (what it does before moving, after moving, on a collision and on dying) and a place in one list, whose order decides which collisions happen first (`element.h`, `process.c`).
- **A side** is UQM's `playerNr`: the player's (`RPG_PLAYER_NUM`, 0) or the enemy's (`NPC_PLAYER_NUM`, 1). UQM's battle has two sides with one ship each at a time.
- **The arena** is the battle's space: a torus 8,192 by 7,680 world units in UQM (`LOG_SPACE_WIDTH`, `LOG_SPACE_HEIGHT`), with one planet and five asteroids.
- **A trace** is UQM's own battle code run by a recording program with the drawing stubbed, its state written each frame, which the port must match.

## Splitting stage 4

UQM's combat is about 8,600 lines of engine (the simulation 5,200, the AI 1,400, the battle screen 2,000) and about 13,900 lines of ships, 28 files from 118 lines (the Ur-Quan drone) to 1,083 (the Orz). Before stage 3b nothing in space can start a battle but the Ilwrath at Earth, so most ships can't be met until then. This plan splits stage 4 in two (decision 1) and plans the first part in detail:

| Part | What | Needs |
| --- | --- | --- |
| **4a. The battle, together** (this plan) | The battle engine checked against UQM's traces; the flagship and escorts in combat (`sis_ship.c`, with each ship's own modules); the Ilwrath ship and its AI; several players against one enemy: battle markers, joining, the bigger arena, each player's camera, status and minimap, retreat, disconnects, wrecks and revival, salvage, game over; the Ilwrath fight in the opening | 3a |
| 4b. Every race's ship and the host's fleet | The other 24 race ships with their AI, each checked against UQM's traces, among them the ones whose code assumes one opponent (Orz, Melnorme, Shofixti, VUX, Pkunk); the host's AI fleet (`built_ship_q`): the shipyard building, scrapping and crewing fleet ships, the Arilou's gift, the host sending one fleet ship into battle instead of the flagship and choosing the next when it dies; the enemy's fleets of several ships, one at a time | 4a |

The Sa-Matra (`lastbat.c`, 926 lines) and the final battle go with the endgame, after 3c: nothing reaches it before then, and its gate, which ends the game for whoever touches it with the bomb, needs the bomb (3c) and a co-op rule of its own.

## Goal of stage 4a

The group fights together:

1. **The Ilwrath at Earth.** In the opening, the Ilwrath ship's talk ends in a battle, as in UQM. The player who talked fights it in UQM's battle: UQM's arena, planet and asteroids, the Ilwrath's hellfire and cloak, its AI, the flagship's or the escort's own guns. A marker shows the battle at Earth, and the others fly into it to help.
2. **Fighting together.** Each player sees the battle from their own ship, zooming as UQM's camera does between them and the enemy, with UQM's status panel for their ship and the enemy's, and a minimap of the arena with every ship.
3. **Losing a ship.** A guest's escort that dies becomes a wreck; a friend revives it by flying to it and giving it crew. The flagship's death ends the game for everyone.
4. **Winning.** The enemy destroyed, the salvage is split between the players who fought to the end. After the starbase is available, a player can retreat as in UQM, for five units of fuel when their ship is the last to leave.

### What the owner will be able to try when it's done

- Start a game with `--start-visit` and reply to the Ilwrath. The battle starts on your screen; the others see its marker by Earth. Fly the flagship, fire its gun, watch the Ilwrath cloak and close in. Win, and the commander's alliance talk follows.
- As a guest, fly into the marker while the host fights: your escort warps in, and both screens follow their own ship. Your shots don't hit the host.
- Lose the escort: it becomes a wreck at Earth once the battle is over; the host flies to it and gives it crew, and you fly on.
- Lose the flagship: every player sees the game over.
- `--start-battle ilwrath` (testing) starts a battle against any ported ship, by the host or with guests flying into it; `--script` takes `fire`, `special` and `retreat`.
- Save, load, quit mid-battle: what each player had is as it was before the battle started.

## Scope

**In stage 4a:**

- **The battle engine:** elements, the list and its order, movement, collisions and their momentum (`process.c`, `collide.c`, `velocity.c`, `displist.c`), gravity and the planet (`gravity.c`), asteroids, explosions and floating crew (`misc.c`), weapons and homing (`weapon.c`), ships' turning, thrust, energy and firing (`ship.c`), dying, warping in and out (`tactrans.c`), and the battle's random numbers, all checked against UQM's traces of one ship against one.
- **Ships:** the flagship (`sis_ship.c`), built from a ship's own modules, so escorts fight with theirs (decision 3), and the Ilwrath (`ilwrath.c`).
- **The AI:** `cyborg.c` and `intel.c`, and the Ilwrath's `ilwrath_intelligence`, choosing among several player ships.
- **Co-op battles:** markers in systems and hyperspace, joining, the arena's size, friendly fire, each player's camera, status and minimap, retreat, disconnects, wrecks and revival, salvage, the battle's end, game over (decisions 2 to 13).
- **The content:** the battle graphics (each ship's hull, weapon and special sheets at three sizes, captain windows, explosions, asteroids, the stars, the planets' battle sprites, the status panel's frames), and the collision masks from them, recorded by UQM's own loader as the lander's were.
- **The Ilwrath fight in the opening** (row 39 closed).
- **The host's pause** (row 12), since a battle is where it matters most (decision 14).
- **Wire and saves:** a battle activity and screen, the battle sent to each player near their ship, a Special key; saves keep wrecks.

**Left out of 4a:**

| Left out | Why | Goes to |
| --- | --- | --- |
| The other 24 race ships and their AI | Nothing can meet them before 3b; each is a port and a trace of its own | 4b |
| The host's AI fleet: building, scrapping and crewing fleet ships at the shipyard, the Arilou's gift, sending a fleet ship instead of the flagship, picking the next ship (`pickship.c`) | Needs the race ships | 4b (rows 43 and 44 stay until then) |
| The enemy's fleets of several ships, one at a time (`BuildBattle`'s queues, `npc_built_ship_q`) | The Ilwrath comes alone; fleets come with the races' encounters | 4b, met in 3b |
| Alien ships flying at players in systems and hyperspace (`encount.c`, `ipdisp.c`, `hyper.c`), the choice to talk or fight | Ships in space are 3b | 3b |
| The Sa-Matra and the final battle (`lastbat.c`) | Needs the bomb and the endgame | The endgame, after 3c |
| Battle sounds and music (`sounds.battle`, `music.battle`, each ship's `ditty`) | Nothing plays sound yet (stage 3's left-out table) | A later stage with audio |
| The Cyborg option (the computer flying the player's ship) and Super Melee | Not part of the co-op game | Not planned |

## How UQM does it

### A frame

`Battle` (`battle.c:396`) seeds the one random generator from the clock (`TFB_SeedRandom(GetTimeCounter())`), so no two battles are alike, sets the arena up (`InitShips`, `init.c:178`: the stars, one planet, five asteroids; none of those in the last battle), and runs `DoBattle` 24 times a second (`BATTLE_FRAME_RATE`).

Each frame, `ProcessInput` turns each side's keys into its ship's input bits, and `RedrawQueue` (`process.c:1013`) steps the whole list:
1. **Before moving** (`PreProcessQueue`): each element in list order runs its death if its life is up, its own `preprocess_func` (for a ship, `ship_preprocess`: energy, turning, thrust, and on the first frame the warp-in), moves by its velocity, and is checked for collisions against the elements after it (`ProcessCollisions`: the time of impact over the sprites' masks, both collision callbacks, then `collide()`'s momentum exchange by mass).
2. **After moving** (`PostProcessQueue`): elements added this frame are stepped and collided too, the disappearing ones go, each runs its `postprocess_func` (for a ship, `ship_postprocess`: firing, the special), and the frame to draw is chosen for the zoom.

The list holds at most 150 elements (`MAX_DISPLAY_ELEMENTS`); a shot that finds it full isn't fired. Which element is first in the list (`InsertElement` puts it at the head, `PutElement` at the tail) changes which of two collisions happens first.

### The camera

UQM's coordinates move with the camera: each frame the camera's scroll is added to every element (`PostProcessQueue`), so the view's centre stays the arena's. The camera is the wrap-aware midpoint of the two ships, zoomed out to keep both in view, from 1× to 4× (`CalcReduction`, `CalcView`; in steps of 1×, 2× and 4× with the step zoom). Since the arena is a torus and the shift is whole display pixels, the physics don't depend on it, except: asteroids that drift off are put back at x 0 or y 0, the seam opposite the camera (`misc.c`), and the stars scroll with it.

### Sides

Collisions and weapons don't look at sides: a shot hits any element but those of the ship that fired it (`IGNORE_SIMILAR`, same `pParent`; `weapon_collision`). Point defence and the AI's threat checks do (`elementsOfSamePlayer`). Homing (`TrackShip`, `weapon.c:319`) turns towards the nearest opposing ship by |dx| + |dy| and keeps it once chosen (`hTarget`); the AI chooses its enemy as the nearest ship by straight-line distance, without a side check (`cyborg.c:1138`). With two ships these agree; with several they don't.

### Ships

A ship is its race's description (`RACE_DESC`: crew, energy, thrust, turning, weapon and special costs and waits, mass, the AI's range) and its callbacks. The flagship builds its description from its modules (`init_sis`, `sis_ship.c:919`): its guns from the weapon modules and where they are (`InitWeaponSlots`), its energy from the dynamos and furnaces, its thrust and turning from the drives and jets, its crew from `CrewEnlisted` plus the captain. The Ilwrath fires short-lived hellfire and cloaks, uncloaking when it fires (`ilwrath.c`, 409 lines).

The status panel has two slots, the enemy's at the top and the player's below (`status.c`): each ship's icon, name, crew and energy bars, and its captain's window, animated as the ship turns, thrusts and fires. UQM draws them from inside the simulation (`DeltaCrew`, `DeltaEnergy`), and a few ships draw on the other's silhouette (`ModifySilhouette`, which also draws a random number).

### Losing, retreating, winning

- **A ship dies** (`ship_death`, `tactrans.c:730`): it explodes over 36 frames, its side's count goes down (`battle_counter`), and its side picks the next ship: the enemy the next in its queue, the player from a menu that stops the battle while they choose (`GetArmadaStarShip`). The flagship's death is game over (`CrewEnlisted = ~0`).
- **Retreat** (`RunAwayAllowed`, `DoRunAway`, `battle.c:64`): only in an encounter or the last battle, after the starbase is available, and not carrying the bomb. The ship flashes red and warps out keeping its crew; it can't be picked again in that battle. If the flagship leaves last, it costs 5 units of fuel, down to no lower than 0 (`RUN_AWAY_FUEL_COST`, `pickship.c`).
- **The end** (`UninitShips`, `UninitEncounter`, `encount.c:476`): the crew still floating goes to the surviving ship. On a victory each enemy ship destroyed pays `RACE_SHIP_COST / 8` resource units (the Ilwrath's 1,000 pays 125). Dead escorts are gone.

### Entering a battle

A talk that ends hostile (`BATTLE_SEGUE`) builds both sides (`BuildBattle`, `encount.c:102`: the enemy's ships, then the player's escorts and the flagship), the planet (`BATTLE_PLANET` in a system, a random one in hyperspace) and the escape counter (`ESCAPE_COUNTER`, 110), and fights (`EncounterBattle`). The Ilwrath at Earth comes this way from its talk, outside an encounter, so it can't be retreated from (`RunAwayAllowed`: no starbase yet).

## Co-op rules for stage 4

Decided in `docs/design.md`:
- Battles show as a marker where they started, in a system or hyperspace, to the players there; flying into it joins (row 15). All players in it fight at once (row 5), against one enemy ship at a time, unscaled.
- Enemies target the nearest opponent (`TrackShip`).
- Each player has their own camera and an arena minimap (row 6), in a bigger arena (row 7).
- A player's ship that dies becomes a wreck; a rescuer revives it by giving it crew, keeping at least 1 (row 8). The flagship's death ends the game for the group.
- Retreat as in UQM, per player; a disconnect mid-battle is a retreat with the same fuel cost, even where UQM forbids it (row 4).
- Salvage split equally between the players in the battle when it's won; retreated and disconnected players get none (row 18).
- Only the host pauses; the pause freezes combat (row 12).
- The battle runs on the host at 24 frames a second; clients send keys with the fixed input delay and draw what they're sent, near their own ship.

## Work, in order

Each item ends with tests that run headless, in a new topic, `combat` (`tests/topics.json`). The engine is checked as the lander was: UQM's own battle code runs in a recording program with the drawing, sound and input stubbed, the seed fixed and the keys scripted, and the port must match it frame by frame.

1. **The battle's data and content.**
   - The ships' descriptions from `data/ships.json` (already extracted, read by nothing yet).
   - The battle graphics by their resource ids (`ship.<race>.graphics.*` at three sizes, `graphics.boom.*`, `graphics.blast.*`, `graphics.asteroid.*`, `graphics.stars`, `graphics.status`, the planets' battle sprites), and their collision masks, recorded by UQM's own image loader (`write_masks`, as the lander's were).
   - Check: every resource the flagship, the Ilwrath and the arena use loads, and the masks match UQM's.
2. **The engine, two ships.** `Combat/`: elements, the list, before and after moving, collisions with their time of impact, `collide()`, gravity, the planet, asteroids, explosions, floating crew, weapons and homing, a ship's energy, turning, thrust and firing, dying and warping in, with UQM's random generator per battle. In UQM's arena with UQM's camera, so a battle of one ship against one is UQM's.
   - Check: traces from UQM's own `process.c`, `collide.c`, `gravity.c`, `misc.c`, `weapon.c`, `ship.c`, `tactrans.c` and `init.c` (a new recording program, `tools/extract/golden/battle.c`, `tests/golden/battle.json`): scripted keys for both ships over hundreds of frames each, seeded; each frame every element's state as Super Melee's netplay checksum takes it (flags, life, crew, mass, turn and thrust waits, velocity, place), the camera's scroll, and the random generator's state. Scenarios: flying into the planet, ramming, shooting, the list full, asteroids drifting off, a ship dying.
3. **The flagship and the Ilwrath.** `sis_ship.c` ported to build a ship's description from that ship's modules, not the one flagship's (decision 3); `ilwrath.c`.
   - Check: traces of the flagship with several module layouts (guns forward and aft, blasters, cannons, tracking, point defence, dynamos and furnaces) against the Ilwrath and against each other, and of the Ilwrath's cloak and hellfire.
4. **The AI.** `intel.c`, `cyborg.c` and `ilwrath_intelligence`; the enemy chooses among the player ships by UQM's own rule (the nearest), and only opposing ships.
   - Check: traces of the Ilwrath flown by UQM's AI against a scripted flagship; tests of our own for its choice among several.
5. **Several ships.** The sides become the players' and the enemy's, with any number of player ships at once (decision 2 for friendly fire, decision 4 for the arena, decision 5 for the frame the world moves with). Each player ship warps in where UQM's `spawn_ship` puts one; homing and the AI choose among them.
   - Check: tests of our own: four player ships against the Ilwrath, shots passing teammates, the AI changing target, joining mid-battle, the arena's size from the setting; UQM's traces still matched with one player and the arena at UQM's size.
6. **Battles in the world.** A battle activity for each ship in it; a battle marker where it started, in the system or hyperspace, shown to the players there; flying into it joins, warping in (decision 6); retreat as UQM allows it, per player, with the fuel cost; a disconnect as a retreat; the battle's end, its salvage and its floating crew (decision 9); the clock runs on (row 1).
   - Check: `GameWorld` tests: starting, joining, retreating, disconnecting, winning and losing with one to four players, salvage and its remainder, a battle no one is left in.
7. **Wrecks, revival and game over.** A player ship that dies becomes a wreck at the battle's place once it's over (decision 7): it doesn't fly, and its player watches the battle, then their wreck. A rescuer flies into it and gives it crew (decision 8). The flagship's death ends the game for the group (decision 10). Saves keep wrecks.
   - Check: tests of the wreck's place, revival keeping the rescuer at least 1, a wreck's player leaving and rejoining, the wreck saved and loaded, the flagship's death.
8. **The Ilwrath fight in the opening.** Its talk's hostile ending starts a battle at Earth for the talk's starter; the opening goes on once it's won (decision 11).
   - Check: `OpeningTests`: the battle starts, won leads to the alliance talk, lost by an escort leaves the Ilwrath at Earth, lost by the flagship ends the game; row 39 closed.
9. **The host's pause.** A key for the host only that stops the world, the battles in it and the clock, shown to every player (decision 14).
10. **The wire.** The battle activity and screen; a Special key (`ShipInput` has two free bits) and the guest's input mask; each player sent the elements near their camera, their own ship and the enemy's status, and the minimap's dots (a new codec, protocol version 20), and reliable messages for a battle's start, end and wreck.
    - Check: `SnapshotCodecTests` round trips and refusals; the network test with a guest joining a battle over the lossy relay.
11. **Client views.** `Presentation/BattleScreen.cs`: UQM's battle drawn as UQM draws it from what the host sends, at each player's camera (decision 12): the stars' three planes, the planet, each element's frame at the zoom's size, explosions, the ion trails; the status panel's two slots, the enemy's and the player's own, with the captain's window, crew and energy; the minimap. Battle markers and wrecks in the system and hyperspace views; the rescue screen; the game over screen.
    - Check: `BattleScreenTests` against UQM's own drawing (the status panel and a frame's sprites, recorded as the starbase's screens were); screenshots.
12. **Docs.** `docs/how-to-try/stage-4a.md`; `design.md`, `porting-inventory.md` (the combat rows), `differences.md` (rows 4 to 8, 12, 15, 16, 18 and 39, and new ones for what 4a decides), `uqm-data.md`.

## Decisions

Recommendations first.

1. **Splitting stage 4.**
   - **Two parts, 4a and 4b, as above; the Sa-Matra with the endgame (recommended).** 4a can be played through (the Ilwrath fight, together), and 4b's ships each plug into an engine already checked.
   - One stage 4: every ship before anything can be played; about twice 4a's size.
2. **Friendly fire.** UQM's shots hit any ship but the one that fired them, and ships bump each other: with several players, they'd shoot each other.
   - **None between players (recommended):** players' shots and specials pass through teammates and their shots, and teammates' ships don't collide. Area effects (the Shofixti's self-destruct, in 4b) spare teammates. Teammates' crew floating in space can be picked up by any player.
   - UQM's rules as they are: teammates hit and bump each other.
3. **Escorts in battle.**
   - **The flagship's battle code with the escort's own modules; escorts may install the flagship's combat modules (recommended).** `sis_ship.c` builds any player's ship from its modules, so an escort with one gun fights like a flagship with one gun. Escorts start with a gun (the escort's kit, a setting), and at the starbase may buy the gun, blaster, cannon, tracking, point defence, dynamo and furnace, within their kit's slots; the flagship keeps the bomb's parts and its other unique upgrades. This sets the combat part of the escort module set (an open item); the rest stays open.
   - Escorts fly a race ship of their own (the Earthling cruiser, say): simpler to balance, but unlike the escort that lands and mines.
4. **The arena's size.**
   - **UQM's arena times a setting, 1.5 each way by default (recommended).** Four ships have room, and each player's camera zooms as UQM's does. Solo battles are a little roomier than UQM's; the traces run at a setting of 1.
   - UQM's size: solo battles exactly as UQM, but four ships crowd.
   - Growing with the players in it: a torus can't grow without moving everything in it.
5. **The frame the world moves with.** UQM's coordinates move with its one camera, and asteroids come back at the seam opposite it.
   - **UQM's frame, following the camera of the battle's first player still in it (recommended):** a battle of one player is UQM's to the frame. Each player's view is their own; the frame only decides the coordinates and the asteroids' seam.
   - Fixed coordinates, the traces converted by their scroll: simpler to reason about, but asteroids would come back at a seam of our own and the traces wouldn't be matched there.
6. **Joining a battle.**
   - **Any player whose ship flies into the marker, in the same system or hyperspace, not in a talk or wrecked, and who hasn't retreated from it (recommended).** They warp in where UQM's `spawn_ship` would put a ship, as UQM's ships warp in. A player whose ship is in it can't leave but by retreat (where UQM allows it) or death.
   - Only players there when it starts: nobody would fly in to help.
7. **Where the wreck goes and what its player does.**
   - **At the battle's place in the system or hyperspace once the battle is over; its player watches the battle on, then their wreck (recommended).** The wreck doesn't fly, land or talk; it keeps its cargo, fuel and modules; it's saved. In hyperspace it stays where it is. Mid-battle the dead ship explodes as UQM's do.
   - The wreck stays in the arena and can be revived mid-battle: closer to "flies to the wreck", but a battle's arena would have to outlast its battle.
8. **Revival.**
   - **A rescuer flies into the wreck and chooses how much crew to give it on a screen like the giving screen, keeping at least 1; the wreck flies on with that crew (recommended).** Anyone not in a battle or a talk can rescue; the flagship can always.
   - Flying into the wreck gives it half the rescuer's crew: no screen, but no choice.
9. **A battle's end, its salvage and its crew.**
   - **Won when the enemy dies with a player ship still in it; lost when no player ship is (recommended).** On a win the salvage (`RACE_SHIP_COST / 8`) is split equally between the player ships in it at the end, the remainder to the one that joined first, as decision 5 of stage 3 split the starbase's gift; the crew still floating is split between them the same way, within each ship's room. A lost battle leaves the enemy where it was (3b's encounters; the Ilwrath, decision 11).
   - All the floating crew to every survivor, as UQM's code would: crew made from nothing.
10. **Game over.**
    - **Every player sees UQM's game over; the host's game then ends, and loading the save goes back to before the battle (recommended).** The host's save is kept as it was, so the group can try again.
    - The game goes on without the flagship: against UQM's story, which is the flagship's.
11. **The Ilwrath fight.**
    - **The talk's starter fights it; the others, freed from the story pause, may fly in; the opening goes on once it's won (recommended).** Losing the flagship is game over, as in UQM; an escort that dies becomes a wreck, and the Ilwrath stays at Earth as a battle marker until a player beats it, the commander's alliance talk waiting until then. No retreat, as in UQM (the starbase isn't available yet), but a disconnect, which retreats.
    - The flagship always fights it, wherever the starter is: UQM's opening is the flagship's, but a guest could start a fight they're not in.
12. **What each player sees.**
    - **UQM's battle screen at each player's own camera, framed between their ship and the enemy as UQM frames two ships; the status panel's two slots, the enemy's and their own; a minimap of the whole arena with every ship, the planet and the wrecks, in the corner of the space screen (recommended).** Teammates' crew and names show by their dots.
    - One shared camera framing every ship: as UQM, but four ships far apart would zoom everyone out.
13. **Saving during a battle.**
    - **Battles aren't saved: on load, a player who was in one is back where it started, outside it, with what their ship had when it began (recommended),** as talks and visits aren't saved now. The Ilwrath's talk, being part of the visit, comes again.
    - Save the battle, every element: a big new save format for little gain.
14. **The host's pause** (row 12) in 4a.
    - **In 4a (recommended):** a key for the host stops the world, every battle and the clock, and every player sees that it's paused. Battles are where pausing matters, and UQM's pause stops one.
    - Later: pausing waits, and a host can't stop a battle to answer the door.

## Risks

- **Trace parity.** One random generator feeds the simulation, the AI, the stars, explosions and the status panel's silhouettes (`ModifySilhouette`); the port must draw the same numbers in the same order, including in code that draws. The recording program records each draw's place, as the talks' did.
- **Drawing mixed into the simulation.** UQM draws the status panel and the captain's window from inside the step (`DeltaCrew`, `DeltaEnergy`, `ship_preprocess`). The port keeps the state and draws it on the client, and the traces record the calls separately.
- **Collision order.** Which collision happens first depends on the list's order and its 150-element cap; the port keeps both, and with four players the cap is reached sooner. The cap is UQM's for one player; with more it's a setting, checked to hold the traces at UQM's.
- **Bandwidth.** A battle can hold 150 elements; sent near each player's camera as deltas, about 2 kilobytes a frame each at worst. The network test measures it over the lossy relay.
- **Size.** 4a ports about 8,600 lines of engine and 1,400 of ships; 4b's 24 ships are about 11,900 more, mostly mechanical once the engine is checked.
