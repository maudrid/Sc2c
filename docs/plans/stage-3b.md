# Stage 3b plan: ships in space and the races

**Status:** a draft for the owner to approve (2026-11-17). Nothing is built. The decisions below each have a recommendation; the plan is written as if they're all accepted.

Build order item: the second part of "3. Story flags, conversations, dialogue locks, story pauses and the group inventory" (`docs/design.md`; stage 3, decisions 1 and 2), after stage 4. The stage 3 plan's table gives 3b: alien ships in systems and hyperspace (`encount.c`, `ipdisp.c` and `hyper.c`'s encounters), among them the Ur-Quan drone at Earth and the Slylandro probes; hailing and the choice to attack or talk; the homeworlds that start a conversation; the races' conversations met those ways, each checked against UQM; and Melnorme trading and credits. `docs/differences.md` rows 27 (worlds that start a conversation) and 40 (the drone) are temporary until this stage.

Stage 3a's engine, screen, lock, rules and thirteen decisions hold here, as do stage 4's fifteen and stage 4b's six, unless a decision below says otherwise. Terms are theirs, and:
- **A hyperspace encounter** is a group of alien ships flying in hyperspace (UQM's `ENCOUNTER`, in `encounter_q`, at most 16).
- **A system group** is a group of alien ships in a solar system (UQM's `IP_GROUP`, in `ip_group_q`): it orbits a world, explores, or flies at the player.
- **Meeting** a group is what happens when it touches a player's ship: the encounter screen, then a talk or a battle.

## Splitting stage 3b

Stage 3b as listed is the size of stage 4 again. The conversation scripts left to port are about 21,000 lines of C in 25 scripts (the commander's and the allied starbase's are built, the Ilwrath's opening branch too); the Melnorme's alone is 1,855, with its own trading screens. Under them sit the engine pieces nothing has run yet: the races' fleets and their spheres, hyperspace encounters (`hyper.c`, about 600 of its 1,747 lines), system groups (`ipdisp.c` 777, `grpinfo.c` 865) and the encounter screens (`encount.c`).

So this plan splits 3b in two (decision 1) and plans the first part in detail:

| Part | What | Needs |
| --- | --- | --- |
| **3b-1. Ships in space** (this plan) | The races' fleets and spheres; hyperspace encounters and system groups, flying as UQM's and meeting the players; the encounter screen (hail or attack) and the battle's end screen; meetings ending in talks and battles. Five races' talks to fly them: the Ur-Quan and their drone at Earth (row 40), the Kohr-Ah, the Ilwrath met anywhere, the Spathi away from home, the Slylandro probes | 3a's engine, stage 4's battles |
| 3b-2. The races | The other 20 scripts (below), the homeworlds that start a talk (row 27) with their infinite fleets, the races' alliances and the ships they give and take, and the Melnorme: trading, credits per player, bio data's worth, the lander upgrades and the information they sell | 3b-1 |

The scripts left for 3b-2: the Arilou (855), the Chmmr (641), the Druuge (926), the Melnorme (1,855), the Mycon (643), the Orz (898), the Pkunk (1,148), the Yehat rebels (449), the Shofixti (652), the Slylandro at home (921), the Spathi at home (1,018), the Supox (708), the Syreen (878), the Talking Pet (841), the Thraddash (954), the Umgah (729), the Utwig (996), the VUX (796), the Yehat (685) and the Zoq-Fot-Pik (975): about 17,600 lines.

## Goal of stage 3b-1

1. **Ships fly in space.** Each race's fleet holds its sphere as UQM's does, growing and shrinking by the day. In hyperspace, groups of the races whose talks are ported appear around each player inside their sphere and fly at them; in a system, groups orbit, explore and fly at players as UQM's do.
2. **Meeting them.** A group that touches a player's ship meets that player: UQM's encounter screen shows the ships, and the player chooses to hail or attack (or, for a race that offers no choice, talks or fights at once). A hail opens the talk, under the race's dialogue lock; a talk ending in a battle, or an attack, starts stage 4's battle against the group's ships, which the others join by flying into its marker. Once it's won, UQM's battle-won screen shows the salvage.
3. **The first races.** The Ur-Quan drone orbits Earth until it has delivered its message, and the commander can then be asked about "the red thing" (row 40 closed). The Ur-Quan, the Kohr-Ah, the Ilwrath and the Spathi talk and fight as in UQM wherever they're met, and the Slylandro probes come once the story ramps them up.

### What the owner will be able to try when it's done

- Start a new game. The drone orbits Earth; fly into it and it talks (an ordinary talk under the Ur-Quan's lock), then goes. Back at the allied starbase, the commander answers about it.
- `--start-encounter RACE[:SHIPS]` (testing, new, by `data/ships.json`'s ids): a group of that race appears by the host in hyperspace or the system and flies at them. Meet it: the encounter screen, hail or attack, the talk, the battle with a friend flying in, the battle-won screen.
- Fly into the Spathi's or the Ur-Quan's sphere in hyperspace (`--start-at` to get there): groups appear day by day and give chase. With two players in hyperspace far apart, each meets them as they would alone.
- In a system inside a sphere: groups orbit the worlds; attack one and the rest turn on the nearest player, as UQM's turn on the flagship.
- Talk a group into leaving (the Spathi), or fight it: the group is gone, or comes back with the crew it had left if everyone retreated.
- Save and load with groups about: they're where they were, with their crew.

## Scope

**In stage 3b-1:**

- **The races' fleets** (`FLEET_INFO`, `avail_race_q`): each race's sphere (its place, strength and growth), its encounter chances in hyperspace and in systems (`RACE_HYPERSPACE_PERCENT`, `RACE_INTERPLANETARY_PERCENT`), its group makeup (`RACE_ENCOUNTER_MAKEUP`) and whether its sphere is known (`StartSphereTracking`), extracted into `data/` and kept in the story's state, saved. The daily event that grows and moves them (`HYPERSPACE_ENCOUNTER_EVENT`: `check_race_growth`), queued by every new game since 3a, runs from here; the story events that move a race (`SetRaceDest` callers) are 3c's.
- **Hyperspace encounters** (`check_hyperspace_encounter`, `AddEncounterElement`, `ProcessEncounter`, `encounter_collision`): spawned each day by UQM's rule, flying as UQM's, meeting a player they touch; the Slylandro probes' one-shot rule and multiplier.
- **System groups** (`grpinfo.c`'s `BuildGroups` and the per-system group memory, `ipdisp.c`'s `ip_group_preprocess`, `NotifyOthers`, `ip_group_collision`): built on entering a system, orbiting, exploring, intercepting, kept with the system.
- **The encounter screen** (`InitEncounter`: the race's ships, Hail or Attack, `DoSelectAction`), **the battle from a meeting** (`BuildBattle` from the group's ships and crew, `EncounterBattle`), and **the end** (`UninitEncounter`: the battle-won screen, the group's survivors and their crew written back, `UpdateShipFragCrew`).
- **Five scripts**, each checked against UQM's own, every path: `urquanc.c` (the Ur-Quan and the drone), `blackurc.c` (the Kohr-Ah), the rest of `ilwrathc.c`, `spathic.c` (the Spathi away from Spathiwa, Fwiffo's talk included, which 3c's Pluto starts) and `slyland.c` (the probes).
- **The drone at Earth** (`gensol.c`'s `init_probe`: a system group orbiting Earth until `PROBE_MESSAGE_DELIVERED`), row 40 closed.
- **Wire, views and saves:** groups in hyperspace and in systems drawn with their race's icons; the encounter and battle-won screens; the fleets, encounters and groups in the save.

**Left out of 3b-1:**

| Left out | Why | Goes to |
| --- | --- | --- |
| The other 20 scripts, and their races' ships in space | Each script is a port and a recording of its own; a race's groups appear once its talk is ported (decision 2) | 3b-2 |
| The homeworlds that start a talk on orbit, and their infinite fleets (row 27) | Each is its race's script (`spahome`, `slyhome`, the Talking Pet's, the Umgah's, ...) | 3b-2 |
| The Melnorme: trading, credits, the lander upgrades, information, and their rescue of a ship out of fuel | Their script is the largest left, with screens of its own | 3b-2 |
| The story events that move fleets or change spheres (the Ilwrath's and the Mycon's missions, the Kohr-Ah's genocide, the Yehat's revolt, ...) and the Arilou's entrance | The event calendar is 3c | 3c |
| QuasiSpace and the Arilou's portal | Needs the Arilou's talk and the portal spawner (3b-2, 3c) | 3c |
| The starmap and its spheres of influence | No starmap screen yet; the known spheres are kept in the state for when it comes | A later stage |
| The encounter music (`REDALERT_MUSIC`) and the battles' sounds | Nothing plays sound yet | A later stage with audio |

## How UQM does it

### Fleets and spheres

Every race has a `FLEET_INFO` in `avail_race_q`: the centre of its sphere (`loc`), its strength (`actual_strength`, the sphere's radius in steps of `SPHERE_RADIUS_INCREMENT`; `INFINITE_RADIUS` for the Slylandro and the Melnorme, met anywhere), its growth, and where it's heading (`dest_loc`, `days_left`, set by story events through `SetRaceDest`). Each day `HYPERSPACE_ENCOUNTER_EVENT` runs `check_race_growth` (spheres grow, shrink and move a day's worth) and, if the flagship is in hyperspace, `check_hyperspace_encounter`.

### Hyperspace encounters

`check_hyperspace_encounter` (`hyper.c:165`) rolls once a day for each race whose sphere holds the flagship: within the radius, a chance of `RACE_HYPERSPACE_PERCENT`. A hit adds an `ENCOUNTER` to `encounter_q` (at most 16), from the race's sphere centre. The Slylandro probes and the Melnorme have their own rules: one at a time, the probes at a multiplier the story ramps up (`SLYLANDRO_MULTIPLIER`), the Melnorme at 30% when the flagship's fuel is gone and 100% when the broadcaster is used.

Each frame `ProcessEncounters` gives every encounter near the flagship an element (`AddEncounterElement`: it warps in from the edge of the view), steers it at the flagship at its race's speed and lets the far ones go. One that touches the flagship (`encounter_collision`) stops, and the game goes to the encounter (`IN_ENCOUNTER`).

### System groups

Entering a system, `BuildGroups` (`grpinfo.c:212`) rolls which race's groups are there: at a race's homeworld always (70%, the Spathi and Supox 2%), elsewhere by the distance from its sphere's centre, and builds 1 to `MAX_BATTLE_GROUPS` groups by the race's makeup. Special systems add groups of their own (`GROUPS_ADD_NEW`, from their generators). The random groups are kept with the day: they go after a week outside the system, or when another system is entered (`GetGroupInfo`, `PutGroupInfo`).

`ip_group_preprocess` (`ipdisp.c:138`) flies each group: orbiting a world, exploring, or intercepting the flagship when the group's task says so; a group that the talk sent away (`IGNORE_FLAGSHIP`) leaves it be. After a talk or a battle, `NotifyOthers` turns the race's other groups in the system on the flagship (`IPNL_INTERCEPT_PLAYER`) or sends them back to their business (`IPNL_ALL_CLEAR`). A group touching the flagship (`ip_group_collision`) starts the encounter.

### The encounter

`InitCommunication` (`comm.c:1304`) builds the enemy's side from the group (`BuildBattle(NPC_PLAYER_NUM)`), loads the race's talk and, if the race lets the player choose (`BATTLE_SEGUE` set by its script's init), shows the encounter screen (`InitEncounter`, `encount.c:262`): the alien ships, with Hail and Attack. A hail runs the talk (`HailAlien`); an attack goes straight to the battle. A talk ending hostile goes to the battle too (`EncounterBattle`); then `UninitEncounter` (`encount.c:476`) shows the battle-won screen with the salvage, writes the surviving ships' crew back into the group (`UpdateShipFragCrew`) and removes the group if none are left.

### The drone

At Sol, `init_probe` (`gensol.c`) adds the Ur-Quan drone as a system group orbiting Earth until `PROBE_MESSAGE_DELIVERED`. Meeting it runs the Ur-Quan's talk as `URQUAN_DRONE_CONVERSATION`; it delivers its message and leaves.

## Co-op rules for stage 3b-1

Decided already (`docs/design.md`, stage 3 and 4 decisions):
- A talk is the answerer's ship's: its names, hold, crew and fuel (the owner, 2026-11-12). Ships a talk gives go to the host's fleet, whoever answers (2026-11-15).
- One player per race in an ordinary talk at a time (the dialogue lock); a second is told who's talking (stage 3, decision 8: "In 3b, a ship hailing a race already in a talk is told the same and left in flight").
- Enemy ships target the nearest opponent. One enemy ship at a time, unscaled.
- Battles show as markers; flying in joins (stage 4, decision 6). Salvage split between the players in the battle when it's won.
- The scripts are recorded from UQM and matched path for path (stage 3, decision 13), with their own random generator per talk (decision 12).

## Work, in order

1. **The fleets' data.** `tools/extract/extract_uqm.py` takes each race's `FLEET_INFO` start (`globdata.c`'s `InitGameStructures`, `races.h`: sphere centre, strength, growth, colour), its hyperspace and interplanetary chances and its group makeup into `data/races.json`, with data checks. The story state holds each race's fleet (strength, place, heading, whether its sphere is known), saved (save version 13).
2. **Spheres and the daily roll.** `HYPERSPACE_ENCOUNTER_EVENT` runs each day: `check_race_growth`, then UQM's roll for each player in hyperspace (decision 3). The shared encounter list, its cap a setting (UQM's 16). Checked against UQM's own: the recording program (`tools/extract/golden/`) runs `check_race_growth` and `check_hyperspace_encounter` over days and places with a seed, and the port matches it for one player.
3. **Encounters in hyperspace.** `AddEncounterElement` and `ProcessEncounter` for every player in hyperspace: each encounter warps in near a player, flies at the nearest player in hyperspace and meets the one it touches (decision 5); far from every player it's let go as UQM's are. Checked against UQM's frames for one player.
4. **System groups.** `BuildGroups` on the first player's entry, the groups kept with the system (decision 7); `ip_group_preprocess`, intercepting the nearest player in the same part of the system (the inner system or the outer); `NotifyOthers`; `ip_group_collision`. The drone at Earth. Checked against UQM's frames for one player.
5. **The meeting.** The encounter screen for the player met, Hail or Attack; the talk under the race's lock (a group whose race is locked by another's talk passes through: decision 5); the battle from the group's ships with their crew (stage 4b's `EnemyFleet`), its marker where they met; `UninitEncounter`'s end, the survivors written back, `NotifyOthers`, the group gone when none are left. Races whose talk isn't ported don't spawn (decision 2). `--start-encounter`.
6. **The Ur-Quan, the drone and the Kohr-Ah.** `urquanc.c` and `blackurc.c` ported and recorded (every path, as 3a's), with their rules in `TalkRules` (decision 6). Row 40 closed.
7. **The Ilwrath and the Spathi.** The rest of `ilwrathc.c` and `spathic.c`, recorded the same way.
8. **The Slylandro probes.** `slyland.c`, the probes' one-shot rule and multiplier (the ramp-up event runs since 3a).
9. **The wire.** Encounters and groups near each player, the encounter screen's choice, the battle-won screen (protocol version up).
10. **Client views.** Groups drawn in hyperspace and systems as UQM draws them (the races' icons); `Presentation/EncounterScreen.cs`: UQM's encounter and battle-won screens, checked against UQM's (`screens.json`).
11. **Docs.** `docs/how-to-try/stage-3b-1.md`; `design.md` (the build order); `porting-inventory.md` (the encounter, system, fleet and script rows); `differences.md` (rows 17, 27, 40, and new rows for this plan's co-op rules); `uqm-data.md`.

## Decisions

Recommendations first.

1. **Splitting stage 3b.**
   - **Two parts, 3b-1 and 3b-2, as above (recommended).** 3b-1 makes space alive and playable with five races; 3b-2 is mostly scripts on a finished engine, in items of two or three scripts each, the Melnorme an item of their own.
   - One stage 3b: about twice stage 4a's size before the groups can be met by anything but the five.
   - Three parts, the Melnorme a part of their own: their trading is mostly screens, but the screens reuse 3a's.
2. **Races whose talk isn't ported yet.**
   - **Their groups don't appear, in hyperspace or systems, until their talk is ported (recommended).** A temporary row in `differences.md` lists them; space is emptier until 3b-2 but every meeting is UQM's.
   - They appear and can only be fought: the encounter screen offers Attack alone. Fuller space, but talks that UQM offers (and alliances) are missing, and fights happen that wouldn't.
3. **Hyperspace encounters with several players.**
   - **Each day, UQM's roll for each player in hyperspace, around that player (recommended).** Each player meets about as many groups as alone; the group as a whole meets more, as more players are more ships to find (no scaling, as the design has it). The list's cap is UQM's 16 for the whole game, a setting.
   - One roll a day for the group, around one player in hyperspace chosen at random: the group together meets what one player would, so a big group sees fewer each.
   - A roll only around the host: guests flying alone would meet nothing.
4. **Whom a group flies at.**
   - **The nearest player it can reach: in hyperspace any player in hyperspace, in a system any player in the same part of it (recommended),** as battles' enemies take the nearest opponent. A player in a talk, a battle, orbit, landing or the starbase isn't flown at, and a wreck isn't.
   - The player whose roll made it (hyperspace) or who entered first (systems): groups would fly past one player to reach another.
5. **Meeting a group.**
   - **The player it touches meets it alone (recommended):** their encounter screen, their choice, their talk under the race's lock. Meanwhile the group stays where it met them, and others touching it are told who's talking. A battle's marker is where they met; others join by flying in. A group whose race is in another player's talk (the lock taken) passes through a player it touches, who is told who's talking, as stage 3's decision 8 says.
   - Everyone near is drawn into the encounter screen and watches the talk, the first to touch it replying: like a story talk for those near, but a player passing by is stopped.
6. **Which of these talks are story.**
   - **None (recommended):** every talk with a race's ships, the drone's too, is ordinary, under the lock: the others go on playing. Their story writes are shared as any talk's are.
   - The first meeting with each race is story: the whole group sees each race introduced, but every new race met stops everyone.
7. **System groups when several players visit.**
   - **Kept per system; the random groups go a week after the last player left (recommended).** UQM keeps one system's random groups, dropped when the player enters another system or after a week away; with several players each in a system of their own, that would drop groups under another player's feet.
   - UQM's one slot: whenever any player enters another system, the last system's random groups go.
8. **How meetings are checked.**
   - **UQM's recordings for one player, tests of our own for several (recommended),** as every stage so far: the daily rolls, the encounters' and groups' flight and every script path match UQM's with one player; the co-op rules (several players, nearest targets, the lock) are our own tests.
   - Tests of our own only: quicker, but nothing would show where the solo game drifts from UQM.

## Risks

- **Randomness with several players.** UQM draws the encounters' and groups' numbers from one generator, in the flagship's order. With several players the draws change (more rolls, groups for several players), so only the one-player game can match UQM's recordings; the co-op game is UQM's rules, not UQM's numbers. The recordings need a recording program that runs `hyper.c` and `ipdisp.c` pieces it hasn't yet: the IP flight's recorder (`tests/golden/ip-flight.json`) is the start.
- **A ship out of fuel.** UQM's Melnorme come to a flagship with no fuel; they're 3b-2's. Until then a player out of fuel in hyperspace waits for a friend to give fuel (if giving fuel is possible by then) or is stuck, as now.
- **The save.** Fleets, encounters and system groups are new state; the save's version goes up (13), older saves getting a new game's fleets and no groups.
- **Size.** About stage 4a's size: four engine pieces, two screens and five scripts (about 3,100 lines of C). 3b-2 is then about 17,600 lines of scripts on that engine, in nine or ten items.
