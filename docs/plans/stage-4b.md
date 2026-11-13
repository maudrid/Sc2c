# Stage 4b plan: every race's ship and the host's fleet

**Status:** draft, waiting on the owner's decisions (see "Decisions"). Stage 4a is built (`docs/plans/stage-4.md`); this plan builds on its engine, its co-op rules and its fifteen decisions, which hold here unless a decision below says otherwise.

Build order item: the second part of "4. Combat" (`docs/design.md`; stage 4, decision 1), before stage 3b, since 3b's encounters and conversations end in battles against these ships and these fleets. The stage 4 plan's left-out table gives 4b: the other 24 race ships and their AI, the host's fleet (building, scrapping and crewing fleet ships at the shipyard, the Arilou's gift, sending a fleet ship instead of the flagship, picking the next ship), the enemy's fleets of several ships one at a time, and crew floating in space with the Syreen. `docs/differences.md` rows 43 (the Arilou's gift) and 44 (the shipyard) are temporary until this stage.

Terms are stage 4's (an element, a side, the arena, the seam, a trace), and:
- **A fleet ship** is one of the host's escort ships of UQM's kind (`built_ship_q`, `SHIP_FRAGMENT`: a race, its crew and its captain's name, nothing else), up to 12 (`MAX_BUILT_SHIPS`). Guests' escorts are not fleet ships: they're players' ships, built like the flagship (stage 4, decision 3).
- **An enemy fleet** is the enemy's side of a battle, one ship or more, fought one at a time (`npc_built_ship_q`, `race_q[1]`).

## Goal

1. **Every race's ship fights as in UQM.** Each of the 24 ships flies, fires, uses its special and is flown by the computer as UQM's is, checked against UQM's traces, as the enemy and on the players' side.
2. **Enemy fleets.** A battle can be against several enemy ships, of one race or more, which come one at a time; the salvage is paid for each one destroyed, as UQM pays it.
3. **The host's fleet.** The host builds, crews and scraps fleet ships at the shipyard as in UQM, and moves crew between them in flight. The Arilou's gift comes into it. In a battle, the host chooses which ship to send, the flagship or a fleet ship, and the next one when it dies or retreats.

Before stage 3b nothing in space starts these battles (only the Ilwrath at Earth does), and only the Earthlings are allied, so the shipyard builds only their cruisers: the owner tries most of this stage with testing shortcuts, as 4a's `--start-battle`.

### What the owner will be able to try when it's done

- `--start-battle RACE` (testing, there now) for every race, by `data/ships.json`'s id (UQM's folder names: `spathi`, `urquan`, `blackurq` the Kohr-Ah, `human` the Earthling, ...): fight it with the flagship, and with guests flying in. `--start-battle RACE,RACE,...` (new) makes an enemy fleet: each ship warps in when the one before it dies, and the salvage on the win is the first ship's race's bounty for each ship destroyed, as UQM's.
- `--start-fleet RACE[:CREW],...` (testing, new, by the same ids): the host's fleet starts with these ships. Like `--start-units` and `--start-devices`, it and `--start-allied` change whatever game is loaded and are saved with it. In `--start-battle`, the host chooses the first ship on UQM's pick screen; when it dies or retreats, the next; choosing the flagship is the last choice. Guests flying in fight as now.
- `--start-docked --start-allied RACE,... --start-units N` (testing, `--start-allied` new, by the same ids): the shipyard's hangar shows the fleet; select on an empty slot opens the list of allied races' ships to build; crew a ship, send its crew away, scrap it as UQM's shipyard does.
- The roster in flight: move crew between the flagship and fleet ships.
- `--start-devices` with the portal spawner, at the allied starbase: the commander's bulletin brings three Arilou ships into the host's fleet.
- Save and load with a fleet: it's kept, with each ship's crew.

## Scope

**In stage 4b:**

- **The 24 race ships** (`ships/*/*.c` but `sis_ship`, `ilwrath`, `lastbat` and `probe`, the Ur-Quan drone, which never fights): each ship's code and its AI, its graphics and masks, checked against UQM's traces, flown by the computer as the enemy and by a player on side 0.
- **Ships that touch other ships**, made to work with several players by stage 4's rules (no friendly fire, the nearest opponent, the seam): the Earthling cruiser's and the Chmmr's point defence, the Shofixti's self-destruct, the Syreen's song and the crew it sets floating, the Orz's marines, the Melnorme's confusion, the VUX's limpets, the Pkunk's rebirth, the Ur-Quan's fighters.
- **Enemy fleets:** a battle built from a fleet of several ships, the next one coming in when one dies (`GetEncounterStarShip` for the computer's side), infinite fleets that bring their ship back (`FleetIsInfinite`, for 3b's homeworlds), the salvage per ship destroyed, each enemy ship's crew left recorded for 3b (`UpdateShipFragCrew`).
- **The host's fleet:** world state with `build.c`'s functions on it (`AddEscortShips`, `CountEscortShips`, `EscortFeasibilityStudy`, `CalculateEscortsWorth`, `RemoveEscortShips`, `RemoveSomeEscortShips`, `CloneShipFragment`, `NameCaptain`), which 3b's conversations call; the Arilou's gift (row 43 closed); saves.
- **The shipyard** (`shipyard.c`'s `DoModifyShips`): building, crewing and scrapping fleet ships (row 44 closed); **the roster** (`planets/roster.c`): crew between the flagship and fleet ships in flight.
- **Fleet ships in battle:** the pick screen (`pickship.c`), at joining and after a death or retreat; the flagship picked last; a fleet ship's death and retreat; the retreat's fuel when the host's last ship leaves.
- **Wire and views:** the fleet in the host's state, the pick screen, the shipyard's hangar and ship list, the roster; each ship's sprites, status panel and captain window.

**Left out of 4b:**

| Left out | Why | Goes to |
| --- | --- | --- |
| Alien ships flying at players in systems and hyperspace, the talk-or-fight choice, battles from conversations | Ships in space are 3b | 3b |
| The races' alliances, and the ships they give or take in talks (the Spathi, Zoq-Fot-Pik, Shofixti, Druuge, Pkunk, Utwig, Supox, Umgah, Orz and others) | Talks are 3b; 4b gives them the fleet functions to call | 3b |
| The Sa-Matra and the final battle | Needs the bomb and the endgame | The endgame, after 3c |
| Battle sounds and music, the Pkunk's insults (a sound) | Nothing plays sound yet | A later stage with audio |
| The computer flying a player's ship (UQM's Cyborg option) and Super Melee | Not part of the co-op game (stage 4) | Not planned |
| The trilinear scaler and the minimap's place | Left for the visual-fidelity pass (the owner, 2026-11-13) | Later |

## How UQM does it

### The ships

Each ship is a `RACE_DESC` (in `data/ships.json`, read by `ShipDescriptions` since 4a) and its callbacks: `preprocess_func` and `postprocess_func` (energy, the special, firing), `init_weapon_func` (its shots, each an element with its own callbacks), `uninit_func`, and `intelligence` (its AI on top of `cyborg.c`). Sizes:

| Lines | Ships |
| --- | --- |
| 280–330 | Syreen 284, Supox 288, Spathi 301, Arilou 303, Druuge 324 |
| 360–440 | Earthling 360, Yehat 369, Mycon 376, Zoq-Fot-Pik 377, Utwig 380, VUX 398, Thraddash 400, Umgah 434, Slylandro 438 |
| 520–660 | Shofixti 521, Mmrnmhrm 527, Androsynth 528, Ur-Quan 554, Kohr-Ah 567, Chenjesu 588, Pkunk 640, Melnorme 658 |
| 790–1,090 | Chmmr 790, Orz 1,083 |

Most use only what 4a ported: missiles (`initialize_missile`), lasers (`initialize_laser`), homing (`TrackShip`), the AI's weapon test (`ship_weapons`). Some walk the whole element list and act on what they find:
- **Point defence:** the Earthling cruiser's (`human.c:202`) fires at any element near it but its own ship's; the Chmmr's satellites' check the side (`chmmr.c:533`).
- **Area effects:** the Shofixti's self-destruct (`shofixti.c:263`) damages every ship and shot in range, its own side's too; the Syreen's song (`syreen.c:178`) takes crew from every ship of the other side in range (`AbandonShip`), which floats and is picked up by whichever ship touches it.
- **A target of their own:** the Orz's marines (`orz.c:559`) fly at a ship of the other side (the last such in the list, as the loop keeps overwriting it), board it and fight its crew, shown on its captain's window (`ModifySilhouette`); the Melnorme's confusion (`melnorme.c:416`) and the VUX's limpets (`vux.c:160`) stick to the ship they hit; the Chmmr's tractor beam pulls the ship it hits.
- **Coming back:** the Pkunk may be reborn when it dies (`pkunk.c`, a random draw), warping back in at a random place with full crew (`new_pkunk`); the Ur-Quan's fighters fly back to their own ship (`urquan.c:374`).
- **Harvesting:** the Slylandro's special (`harvest_space_junk`, `slylandr.c:310`) walks the list for asteroids near it and turns them into energy.
- **Moving at random:** the Arilou's teleport (`arilou.c:280`) puts it at a random place in the arena.
- **Warping in:** in Super Melee the VUX appears close to the enemy (`vux_preprocess`); not in an encounter (`IN_ENCOUNTER`), so not in the full game.

### Fleets in a battle

`BuildBattle` (`encount.c`) builds each side's queue (`race_q`) of `STARSHIP`s: the enemy's from the encounter's ships with their crew, the player's from the escorts then the flagship (`built_ship_q`, then the SIS last). `battle_counter[side]` counts the ships each side has left.

`GetEncounterStarShip` (`pickship.c:305`) picks each side's next ship, at the start and whenever its ship dies or retreats:
- **The computer** takes the next in its queue; an infinite fleet (`FleetIsInfinite`: a homeworld's) reuses the dead ship with full crew and a new captain.
- **The player** with no escorts gets the flagship. With escorts, the battle stops for UQM's pick screen (`GetArmadaStarShip`, `DrawArmadaPickShip`): the escorts with their crew and the flagship, and the salvage so far. Choosing the flagship makes it the last choice (`battle_counter = 1`). When the last ship goes: dead, game over (`CrewEnlisted = ~0`); fled, 5 units of fuel.

A fleet ship that dies is gone; one that retreats keeps its crew and can't be picked again in that battle. The salvage (`UninitEncounter`) takes the encounter's race from the first ship in the enemy's queue (`EncounterRace`, `encount.c:540`) and pays that race's `ship_cost / 8` for every enemy ship destroyed, whatever its race (`encount.c:659`); a first race not in `avail_race_q` pays nothing and shows no tally (`encount.c:543`). The player can't be on the pick screen once the enemy has no ships left (`GetArmadaStarShip` returns at once). When the battle ends, each escort keeps the crew it has (`UninitShips`), and the enemy's survivors keep theirs (`UpdateShipFragCrew`).

### The host's fleet

`built_ship_q` holds up to 12 `SHIP_FRAGMENT`s, each in a hangar slot (`index`, 0 to 11). `build.c` adds ships (`AddEscortShips`, into the first free slots; `CloneShipFragment` with a captain named by `NameCaptain`), counts and removes them, says whether there's room (`EscortFeasibilityStudy`) and what they're worth (`CalculateEscortsWorth`, for the commander's advice). Conversations call these to give and take ships (the Arilou's gift is the starbase's bulletin, `starbas.c:1418`: three Arilou ships, once the portal spawner is found).

The shipyard (`DoModifyShips`, `shipyard.c:1207`): the cursor moves round the flagship and the twelve slots; select on an empty slot opens the list of allied races' ships (`GOOD_GUY`), each spinning with its cost; building one costs its `ship_cost` and gives it one crew; up and down on a ship hire and send away crew at the crew price (`CrewTransaction`, as the flagship's: each one sent away gives back the price, less 2 once the crew bought has reached `CREW_EXPENSE_THRESHOLD`), up to its maximum. Sending away the last crew gives back the ship's cost but leaves it in its slot with no crew (`shipyard.c:861`); hiring from there buys it back at its cost (`shipyard.c:818`); leaving the crew editing with it at no crew scraps it (`DMS_ScrapEscortShip`, `shipyard.c:1123`).

The roster (`DoModifyRoster`, `roster.c`), from the flagship's menu in flight: moves crew one at a time between the flagship and a fleet ship, within each one's maximum.

## Co-op rules for stage 4b

From `docs/design.md` and stage 4:
- **One enemy ship at a time**, unscaled; all the players in the battle fight it (rows 5 and 15). Enemies target the nearest opponent.
- **No friendly fire** (stage 4, decision 2): teammates' shots, point defence and area effects pass by teammates; teammates don't bump.
- **The host's fleet:** "Host's AI fleet: stays as in the original. Host can send one fleet ship into battle instead of the flagship." Retreat: "AI fleet ships that retreat are out for that battle; the 5-unit cost applies when the flagship leaves." The design's "AI fleet" is the fleet of allied races' ships that UQM calls escorts (decision 1 settles who flies them).
- **Salvage** split equally between the players in the battle when it's won (row 18).

## Work, in order

Rules for every ship's port (items 2 to 4), from 4a's notes:
- Contact, reach and aim in ship code and its AI are measured across the seam (`Battle.Intersect`, `SeamDeltaX`/`SeamDeltaY`; stage 4 decision 5), not by raw differences: the Slylandro's harvest of asteroids (`slylandr.c:310`), the AIs of the Zoq-Fot-Pik (`zoqfot.c:299`), Umgah (`umgah.c:240`) and Androsynth (`androsyn.c:353`), and any other found while porting.
- A ship that sets `SEEKING_SPECIAL` (the Arilou, Spathi, Chenjesu, Ur-Quan, Chmmr, Orz and VUX) gives its special shots the very `Views` instance in its `RaceDesc.Special`, and has a `Special` even when its special elements take no sheets of their own: the AI tells them from its weapon's by that instance (stage 4, item 3's note).
- A ship that puts itself at a random place (the Arilou's teleport, `arilou.c:280`; the Pkunk's rebirth) draws it across the whole arena, as UQM's does across its own.

Each item ends with headless tests in the `combat` topic, or a new `fleet` topic for items 6 to 8 (`tests/topics.json`). The ships are checked as 4a's were: UQM's battle code in the recording program (`tools/extract/golden/battle.c`), seeds fixed and keys scripted, the port matching each frame's state.

1. **Every ship's content and recording.** Each ship's graphics at three sizes, captain windows and masks, as item 1 of 4a did for the flagship and the Ilwrath; `BattleKit.Enemies` and the battle's shapes made for every race. The recording program gains each ship: a duel against the flagship with the player's keys scripted to use its weapon and special, and a duel of two computer-flown ships, so its AI is traced too. Check: every ship's resources load and its masks match UQM's.
2. **Ships, part 1** (about 2,800 lines): the Arilou, Spathi, Supox, Druuge, Yehat, Zoq-Fot-Pik, Thraddash and Umgah, each with its AI, matching its traces.
3. **Ships, part 2** (about 4,000 lines): the Utwig, Mycon, Androsynth, Mmrnmhrm, Chenjesu, Slylandro, Ur-Quan and Kohr-Ah, matching their traces; the Ur-Quan's fighters and the Kohr-Ah's spinning blades home on the nearest opponent (`TrackShip`, as now), and the fighters fly back to their own ship.
4. **Ships, part 3** (about 4,700 lines): the Earthling, Chmmr, Shofixti, Syreen, Orz, Melnorme, VUX and Pkunk, matching their traces, and made to work with several players (decision 6):
   - point defence (the Earthling's, the Chmmr's) leaves teammates be, as the flagship's does (`Battle.Teammates`);
   - the Shofixti's self-destruct spares teammates;
   - the Syreen's song takes crew from every opposing ship in range; any ship picks up floating crew, and what's left at the end is shared as item 6 of 4a shares it;
   - the Orz's marines go for the nearest opposing ship, and a ship with marines aboard that a teammate's shots can't hurt still loses crew to them;
   Tests with two and four players against each, and each flown by a player against the Ilwrath.
5. **Enemy fleets.** `GameWorld.StartBattle` builds the enemy's side from a fleet (`BattleStart`): races with crew, one at a time, the next warping in when one dies; infinite fleets bring their ship back with full crew and a new captain (the 4a notes: `NewShip` makes a dead ship unavailable, so this brings UQM's recycling back); the salvage is the first ship's race's `ship_cost / 8` for each enemy ship destroyed, as UQM's (`EncounterRace`), none when that race isn't one of `avail_race_q`'s, split as now; the battle's markers show the fleet's first race. A battle lost leaves the enemy fleet with the crew it has (`UpdateShipFragCrew`, recorded for 3b). `--start-battle RACE,RACE,...`.
6. **The host's fleet.** World state: up to 12 fleet ships (race, crew, captain's name, hangar slot), the host's; `build.c`'s functions on it, behind the talks' `TalkShip` (the host's fleet for the host, none for guests: `EscortsWorth`, `EscortRoom` give theirs now, 0); the Arilou's gift from the commander's bulletin (row 43 closed); saves (version 12, an old save loads with no fleet); the captain names drawn as UQM draws them; `--start-fleet` and `--start-allied`.
7. **The shipyard and the roster.** The shipyard's hangar as UQM's (row 44 closed): select on an empty slot opens the list of allied races' ships, with their cost, to build one from the host's wallet; up and down on a fleet ship hire and send away crew at the group's crew price, with UQM's refunds; the last crew sent away gives the ship's cost back, hiring again buys it back, and leaving it at no crew scraps it (decision 3: the host only). The roster from the host's menu in flight. Recorded against UQM's shipyard for the prices, the slots and the cursor.
8. **Fleet ships in battle.** On joining a battle with fleet ships, and when the ship they fly dies or retreats, the host chooses on the pick screen (`GetArmadaStarShip`): the fleet ships still able to fight with their crew, and the flagship; choosing the flagship is the last choice (decision 2 says whether the battle waits). A fleet ship flown by the host is the host's ship in the battle: their camera, status, the enemy's targets, the battle's moving frame (the 4a note: `FollowedElement` follows the host's current ship). A fleet ship that dies is gone (decision 4); one that retreats keeps its crew and is out for that battle; when the host's last ship leaves, 5 units of the flagship's fuel; the flagship's death is still game over; the salvage is one share for the host whatever they flew (decision 5). Saves during a battle bring fleet ships back as they joined it, as stage 4's decision 14 brings players' ships back; one destroyed before the save is gone.
9. **The wire.** Protocol 23: the host's fleet in the host's state (crew, slots) for the shipyard, the roster and the pick screen; the pick screen's choice as a reliable message; the shipyard's and the roster's actions; every ship's elements in the battle snapshot as now.
10. **Client views.** Every ship's sprites, status panel and captain window (`BattleScreen`, as 4a's for the flagship and the Ilwrath, with the Orz's marines and the VUX's limpets on the captain's window, `ModifySilhouette`); the pick screen (`DrawArmadaPickShip`); the shipyard's hangar with its fleet ships, the race list with the spinning ship (`SpinStarShip`) and the crew counts (`ShowShipCrew`); the roster. Drawing checked against UQM's as 4a item 12's was.
11. **Docs.** `docs/how-to-try/stage-4b.md`; `differences.md` rows 5 (the enemy's fleets), 43 and 44 closed, new rows for the decisions (decision 2's change to stage 4's decision 9 among them); `design.md` (the build order's 4b, the fleet row as decision 1 settles it); `porting-inventory.md` (the ships, the shipyard, the roster, the pick rows; its "AI-flown fleet ships" in the Combat AI row as decision 1 settles it); `Options.cs`'s usage.

## Progress

| Item | State | Where |
| --- | --- | --- |

## Decisions

Recommendations first.

1. **Who flies a fleet ship.** The design calls the fleet "the host's AI fleet" and says the host "can send one fleet ship into battle instead of the flagship"; the porting inventory's Combat AI row speaks of "AI-flown fleet ships".
   - **The host flies it, as in UQM (recommended).** The host chooses which ship goes in and flies it; the flagship waits outside the arena until chosen. "AI fleet" is read as the allied races' ships, which UQM's player flies.
   - The computer flies fleet ships beside the host's flagship: a second ship on the players' side the host doesn't steer, not UQM's game, and more ships against one enemy.
   - Any player may fly one of the host's fleet ships (a guest whose escort is wrecked, say): more to do for a guest without a ship, but against "the host can send one".
2. **Choosing the next ship mid-battle.** UQM stops the battle while the player chooses.
   - **The battle stops while the host chooses only if no other player ship is fighting in it; otherwise it runs on, and the host chooses while the others fight (recommended).** Alone, the host gets UQM's battle; with others, nobody's fight freezes for one player's choice. A guest flying in while it's stopped joins when the host has chosen. This changes stage 4's decision 9 (lost when no player ship is in it): a host on the pick screen counts as in the battle, so a battle with the host choosing and nobody fighting isn't lost, and if the enemy's last ship dies meanwhile, it's won, the pick screen closes and the host has a share (UQM's pick screen can't be open then). A `differences.md` row says so.
   - It always stops for everyone in it: UQM's, but every guest waits on the host's every death.
   - It never stops: the enemy flies on with nobody to fight while the host chooses alone.
3. **Who has a fleet, and who changes it.**
   - **Only the host has a fleet; only the host builds, crews and scraps its ships, from the host's wallet and at the group's crew price (recommended).** Guests at the shipyard see the hangar and crew their own ship as now. Guests can give the host units. Ships that talks give (3b) go to the host's fleet whoever answers, as the Arilou's do.
   - Each player has a fleet of their own: guests' escorts would carry fleets, beyond "the host's AI fleet", and 3b's talks would give ships to whoever answers.
   - Only the host has a fleet, but any player may build into it with their own units.
4. **A fleet ship that dies.**
   - **It's gone, as in UQM (recommended).** Wrecks and revival are for players' own ships (stage 4, decisions 7 and 8).
   - It becomes a wreck that the host can crew back: kinder, but UQM's fleet ships are cheap to rebuild and the rule is for players.
5. **The salvage with fleet ships.**
   - **One share per player in the battle when it's won, whatever ship they fly (recommended).** The host's fleet ship counts as the host. Stage 4's rule split it between player ships, the same thing while each player had one.
   - One share per ship: the host would take a bigger share for having a fleet.
6. **Ship code that picks "the" opponent.** Most ship code already takes the nearest (`TrackShip`). Where UQM's loop takes the last opposing ship in the list (the Orz's marines), or acts on every ship in range (the Syreen's song, the Shofixti's self-destruct), with several players:
   - **A single target is the nearest opposing ship, as homing's; effects on every opposing ship hit every player ship in range; effects UQM aims at any ship spare teammates (recommended).** With one player each is UQM's, so the traces hold.
   - UQM's list order as it is: the marines would pick a player by when they joined.

## Risks

- **Size.** About 11,500 lines of ships, each with traces; parts of 2,800 to 4,700 lines are each about the size of 4a's items 2 and 3.
- **The golden traces' size.** 4a's ship traces are 5 MB for 22 battles; two duels for each of 24 ships would add about 10 MB more. If that's too much for the repository, the recording keeps fewer fields per frame (the state the port could get wrong), or shorter duels that still use every weapon and special.
- **Ships that act on the whole battle.** The Orz (1,083 lines, marines aboard other ships, its turret), the Chmmr (satellites, tractor, point defence) and the Pkunk (rebirth) do things 4a's engine hasn't seen; each may show where the port's engine differs from UQM's in ways the flagship and the Ilwrath didn't. They're in the last part so the engine is settled before them.
- **Drawing from the simulation.** The Orz's marines and the VUX's limpets draw on the captain's window and draw random numbers while doing it (`ModifySilhouette`); the port keeps that state for the client, as 4a did for the status panel.
- **The pick screen and the network.** A host choosing while guests fight must not stall the battle; the choice is a reliable message, and the battle meanwhile has one fewer player ship.
