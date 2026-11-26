# Stage 3c plan: story events and devices

**Status:** drafted 2026-11-27 (the owner's go-ahead of 2026-11-26 to move on to 3c); its questions (at the end) are open. Nothing is built before the owner answers them and gives an item its go-ahead.

Build order item: the third part of stage 3 (`docs/design.md`; `docs/plans/stage-3.md`, decision 1): "the event calendar, energy nodes on story worlds, devices, time skips, cutscenes. After 3b." 3a built the story state and the starbase, 3b-1 the clock's events, the fleets and the meeting, 3b-2 every race's script. 3c makes the story move: the events those scripts queue run, the story worlds give up what's on them, the devices do what they do, and the places 3b-2 left with no way in (QuasiSpace, the Talking Pet's world, the Chmmr) open. What's left after 3c is the endgame: the Sa-Matra and the final battle (`design.md`'s build order).

The decisions of stage 3a, stage 4, stage 4b, 3b-1 and 3b-2 hold here unless a question below changes one. Terms are 3b-1's (`docs/plans/stage-3b.md`): a hyperspace encounter, a system group, meeting a group.

## Goal

1. **The calendar runs.** Every one of UQM's 18 events runs as `gameev.c` has it. The fleets move on their missions, races die, the Spathi shield themselves, the Shofixti come back, the Arilou's portal opens each month, and the Kohr-Ah win if the players are slow. The events set aside since 3a run (question 1), and `differences.md` row 66 closes.
2. **The story worlds' pickups.** Every energy node UQM puts on a story world (19 generators), with its report, as `gen*.c` has it. The artifacts are picked up, the ruins tell what they tell, the wreck gives the portal key, the vault opens, and Fwiffo is found on Pluto.
3. **Devices are used.** The hyperspace and system menus come, each with its Devices entry, and the orbit menu's Devices entry stops saying "later". Each device with a use does what `devices.c` has it do (question 5).
4. **The other time skips.** The Talking Pet's 15 days and the bomb's 14 are story pauses for everyone, as the starbase's two weeks are (`stage-3.md`, decision on skips; row 3).
5. **The places 3b-2 left shut.** QuasiSpace, with the Arilou at home. The Talking Pet's world. The Chmmr: freed, then the bomb ship built and taken to Earth. The Syreen past their first talks follow from these (the egg cases, the vault, the Mycon's ambush).
6. **The story's "cutscenes".** UQM has no slides mid-game (`ShowPresentation` runs only for the intro, the ending and the 3DO ship spins, row 63). Its mid-game cutscenes are fades and flashes: the skips' fades, the Sun Device's white flash, the portal opening and the spin into it, the Utwig bomb's flash. Each comes with its item, shown to whoever UQM would show it to (a story pause shows it to all).

### What the owner will be able to try when it's done

- Play on past the opening without the story standing still. Make an alliance with the Zoq-Fot-Pik and hear months later that they're in trouble; save them or don't. The Spathi hide behind their shield six months after their alliance, and the Shofixti come back two months after Tanaka is recruited.
- Send the Thraddash, the Mycon, the Utwig and the Supox off on their missions and watch their spheres move (in the status panel's fleet lines; there's no star map, question 8). The Ilwrath go to war with the Thraddash.
- Wait four years: the Kohr-Ah begin killing the races one by one, and the game ends if they kill them all.
- Land on the story worlds and pick up what's there: the Maidens, the VUX beast, the Ultron's parts, the Sun Device, the bomb, the Aqua Helix, the Taalo protector, the egg cases, the broadcasters, the portal key. Read the ruins, the Androsynth's three chapters among them. Find Fwiffo on Pluto (it costs lander crew), and bring him into the host's fleet.
- Open the Devices list in hyperspace, in a system or in orbit, and use what's in it: mend the Ultron, call the Melnorme with the broadcaster, talk to the Ilwrath's "gods", set off the Utwig bomb (question 6).
- On the 17th of a month, find the Arilou's portal and fly through to QuasiSpace; fly to the Arilou at home, or out through a vortex. Open a portal of your own with the spawner (question 7).
- Orbit the Umgah's world once the Arilou have told you about the zombies; take the Talking Pet aboard, or be hypnotised and wake up 15 days later in front of the Ur-Quan.
- Free the Chmmr with the Sun Device and come back with the Pet and the bomb: the host's flagship is rebuilt as the bomb ship and flown to Earth, two weeks pass (question 9).
- Reach every one of these with the testing flags too: `--start-state`, `--start-devices`, and a new `--start-date` to put the clock where an event falls (item 1).

## What the research found

Four research passes over UQM's source and the port (2026-11-27): the event calendar; devices and time skips; the story worlds' energy nodes and pickups; QuasiSpace, the Chmmr, cutscenes and where the endgame starts.

### The events

`gameev.c` has 18 events. A new game queues four: the daily `HYPERSPACE_ENCOUNTER_EVENT`, the Arilou's portal on 17 March 2155, the Kohr-Ah's victory four years on, and the Slylandro's ramp-up. The port runs three of them (the daily one, which moves the fleets by `check_race_growth`, and the Slylandro's two ramps). Every other event is set aside on its date (`EventQueue.SetAside`, saved since version 13): `StoryState.RunEvent` throws for them, and that's 3c's seam. A fleet's arrival is an event too, run by `check_race_growth` the day the fleet arrives; the port sets those aside as well.

| Event | Queued by | What it does | Kind |
| --- | --- | --- | --- |
| `ARILOU_ENTRANCE_EVENT`, `ARILOU_EXIT_EVENT` | A new game; each other | The portal opens on the 17th of each month for three days (`ARILOU_SPACE`) | Story field; the portal (item 9) |
| `ARILOU_UMGAH_CHECK` | The Arilou's talk, 10 days on | `ARILOU_CHECKED_UMGAH` 2 | Story field |
| `SHOFIXTI_RETURN_EVENT` | Tanaka's talk, 2 months on | The Shofixti allied; the crew price 2 lower, its rise counted afresh | Standing, shared price |
| `SPATHI_SHIELD_EVENT` | The Spathi at home, 6 months on | The Spathi un-allied, `SPATHI_SHIELDED_SELVES`, their fleet gone | Fleet; waits while a player is in their system |
| `ZOQFOT_DISTRESS_EVENT`, `ZOQFOT_DEATH_EVENT` | The Zoq-Fot-Pik's alliance, 3 months on; the distress, 6 months on | The distress (and the Kohr-Ah's ship at their world, `genzoq.c`), then their death unless rescued | Fleet; both wait while a player is in their system |
| `YEHAT_REBEL_EVENT` | The Yehat's revolt, at once | The royalists cut to two thirds, the rebels' fleet made, their sphere tracked | Fleet |
| `ADVANCE_PKUNK_MISSION` | The Pkunk's talk | Their migration to the Yehat, turned home by a good reason, or absorbed (`YEHAT_ABSORBED_PKUNK`) | Fleet mission |
| `ADVANCE_THRADD_MISSION` | The Thraddash's talk, at once | To the Kohr-Ah, half their strength lost, home; then the Ilwrath's war if it's begun | Fleet mission |
| `ADVANCE_ILWRATH_MISSION` | The Ilwrath's "gods"; the Thraddash's mission | War on the Thraddash: the Aqua Helix unguarded, both races dead by its end | Fleet mission |
| `ADVANCE_MYCON_MISSION` | The Mycon's talk, at once | To Organon, half lost, `MYCON_KNOW_AMBUSH` | Fleet mission |
| `ADVANCE_UTWIG_SUPOX_MISSION` | The Utwig's talk, at once | Both to the Kohr-Ah, half lost over 160 days, home | Fleet mission |
| `KOHR_AH_VICTORIOUS_EVENT` | A new game, 4 years on | The genocide now, or a year later if the Utwig and Supox went to war | Starts the genocide |
| `KOHR_AH_GENOCIDE_EVENT` | The victory; each Kohr-Ah arrival | The nearest living race killed (the Druuge first), `KOHR_AH_FRENZY`; with none left, Sol and the game over (`KOHR_AH_KILLED_ALL`, the Black Ur-Quan's last talk) | Fleet; game over; waits while a player is at the Sa-Matra |

What it needs that the port lacks:
- **`RunEvent`'s 15 other cases**, each recorded against UQM's own `gameev.c`. `tools/extract/golden/clock.c` already links it and records the daily fleets.
- **Location gates.** Four events wait 7 days while "the player" is in a given system (`gameev.c:76-105, 132-135`). With several players a rule is needed (question 2). The port's clock runs in orbit, on planets and in talks as well (row 1), so a player sits in a system more often than UQM's does.
- **The set-aside events** (question 1). UQM runs only events dated today; one in the past would block its queue. The port runs anything due on or before today, and has kept 3c's events aside with their dates. When 3c runs them, their own re-queues count from that day.
- **Events run inside talks.** Six events run at once from a talk (the Utwig's, the Thraddash's, the Ilwrath's, the Mycon's, the rebels', the Pkunk's good reason). They change fleets every player meets, so each is a notice, and a talk's take-back (an opening undone) has to cover the fleets as the starbase's two weeks already do.
- **A third game over.** The port's game over is the host's flagship lost or surrendered (`GameWorld.LoseTheGame`). The genocide's is the Kohr-Ah reaching Sol; the talk it ends with is ported (`BlackUrquanScript`, `KOHR_AH_KILLED_ALL`).
- **The Arilou's counter.** `ARILOU_SPACE_COUNTER` animates the portal opening each hyperspace frame (`starcon.c:58-77`); it's shared, so it steps once per world frame.
- **No fleet on the wire.** Fleets aren't sent to clients and there's no star map, so a player sees a fleet move only by meeting it. The status panel can list what the player knows (question 8).

### The story worlds

UQM has 19 generators with energy nodes. The port has every world's node layout already (`Systems/Surface.cs`, `EnergyNodes`, made for the scan), but puts only Luna's Lunar base on the surface and runs only its pickup (`Story/StoryWorlds.cs`). 3b-2 wired every guard (`GameWorld.WorldTalks.cs`), so each guarded world already lets a ship into orbit once its guard is gone: 3c needs only the surface side.

| Where | What | Pickup sets | Report |
| --- | --- | --- | --- |
| Sol, Pluto | Fwiffo | Costs the lander 8 crew first (a lander with 8 or fewer dies, and nothing happens); `FOUND_PLUTO_SPATHI`; when the lander's back, a Spathi ship with Fwiffo comes and talks (`scan.c:586-639`) | fwiffo; takes off |
| The Maidens' world | The Shofixti Maidens | `SHOFIXTI_MAIDENS`, `MAIDENS_ON_SHIP` | maidens; takes off |
| The VUX beast's world | Zex's Beauty (a life node) | `VUX_BEAST`, `VUX_BEAST_ON_SHIP` | vuxbeast; takes off |
| Spathi's moon | The Umgah broadcaster | `UMGAH_BROADCASTERS`(`_ON_SHIP`) | umgahcaster; takes off |
| The bomb's moon | The Utwig bomb | `UTWIG_BOMB`(`_ON_SHIP`); the Druuge turn hostile (`DRUUGE_MANNER` 1) | utwigbomb; takes off |
| The Taalo protector's moon | The protector | `TAALO_PROTECTOR`(`_ON_SHIP`) | taalodevice; takes off |
| The Aqua Helix's world | The Helix | `AQUA_HELIX`(`_ON_SHIP`), `HELIX_UNPROTECTED` | aquahelix; takes off |
| The Sun Device's world | The Sun Device | `SUN_DEVICE`(`_ON_SHIP`) | sundevice; takes off |
| Three shattered worlds | Egg cases 0 to 2 | `EGG_CASEn_ON_SHIP` | eggcase; takes off |
| Burvixese moon | Their broadcaster | `BURVIXESE_BROADCASTERS`, `BURV_BROADCASTERS_ON_SHIP` | burvixcaster; takes off |
| The Supox's, the Druuge's, the Pkunk's homeworlds | 16 ruins, the Ultron, the Rosy Sphere and the Clear Spindle in any of them | The artifact, the first time (`ULTRON_CONDITION` 1, `ROSY_SPHERE`, `CLEAR_SPINDLE`) | The artifact's text, then the next; the ruins stay |
| Nine homeworlds, Burvixese | 16 ruins | Nothing | ruins (one per visit) |
| The Androsynth's world | 16 ruins | One chapter each new ruin (bits of the retrieved mask) | Three chapters, then nothing |
| The Ur-Quan wreck | The wreck (never taken) | `PORTAL_KEY`(`_ON_SHIP`) the first time | Two texts |
| The Syreen's vault | The vault (never taken) | Seen: `KNOW_SYREEN_VAULT`; with the shuttle aboard: `SHIP_VAULT_UNLOCKED` | Three texts by state |
| Procyon's starbase moon | (no landing) | Nothing | chmmrbase, shown on approach |

The homeworlds' ruins are reachable only once their race is dead (their talk comes first), so the three artifact ruins are UQM's fallback for what those races give in talks.

What it needs that the port lacks:
- **Reports with more than one entry.** UQM's report text lives for one orbit visit: `landerReport` shows it once, `landerReportCycle` steps through its entries. The port's `LanderReport` has no entry, and the client shows `texts[0]`. Each ship in orbit needs its own place in the text, and a report can come without the lander taking off (the ruins, the vault).
- **Pickups that leave the node.** The ruins, the wreck and the vault stay; today a report is queued only when a node is taken.
- **The surface sheets** for 12 more nodes (Fwiffo, ruins, the Maidens, the casters, the Helix, the Sun Device, the egg case, the Taalo device, the vault, the wreck, the bomb), drawn and touched as the moonbase is.
- **A life pickup with a report** (the beast).
- **Fwiffo's crew loss** over 8 steps of 1/20 s, one lander's, while the world goes on. UQM's injury here never shields (`LANDER_INJURED` 4 lies past its 4-bit shield field); the port's upgrade bit 4 is the speed, so the port must not read it as a shield.
- **The nodes on the scan following the story** (Pluto's after `FOUND_PLUTO_SPATHI`, which has no retrieved bit).

### The devices

UQM opens the Devices list from the hyperspace menu, the system menu and the orbit menu. The port has only the orbit menu's (`Systems/OrbitMenu.cs`, select says "later"): it has no hyperspace or system menu at all (`porting-inventory.md`, the hyperspace menu row). Most devices do nothing when used; they're read by talks. Those with a use:

| Device | Where | Use |
| --- | --- | --- |
| Portal spawner | Hyperspace (not QuasiSpace), 10 fuel units | Opens a portal at the ship (`PORTAL_COUNTER`); while it's open no star can be entered. Through it, QuasiSpace |
| Talking Pet | Anywhere the list opens | The Pet's talk (under the Umgah's lock). At the Sa-Matra it confuses the Ur-Quan (the endgame's) |
| Utwig bomb | Anywhere | Self-destruct: a white flash and the game over (`CrewEnlisted = ~0`) |
| Sun Device | Orbit | A white flash; at the Chmmr's world, the Chmmr freed (their talk, `CHMMR_UNLEASHED`, the shield down) |
| Rosy Sphere, Aqua Helix, Clear Spindle | Anywhere, with the broken Ultron | One more piece of the Ultron mended (`ULTRON_CONDITION`); fails without it |
| The two broadcasters | Hyperspace, or a system | In hyperspace, the Melnorme called (`USED_BROADCASTER`, already each player's own since 3b-2). In orbit of the Chmmr's world before they're freed, their shielded talk (and `CHMMR_BOMB_STATE` 1). In the Ilwrath's system while they live, their "gods" talk (and their mission); with an Ilwrath group first in a system's groups, its talk. Fails elsewhere |

What it needs that the port lacks:
- **The hyperspace and system menus** (question 4).
- **Device talks.** UQM starts them by a fake load and `GLOBAL_FLAGS_AND_DATA` (0 the Pet, bits 4 and 5 the Ilwrath, the rest the Chmmr). In the port a device's talk is a meeting with no group, the user's.
- **`PORTAL_COUNTER` made each ship's own** (question 7). It's a shared story field now; shared, one player's spawner would open a portal under every ship in hyperspace and stop all of them entering stars.
- Who may use a device, and whether the group must be there: `design.md`'s open item, deferred since the survey (question 5).

### Time skips

`MoveGameClockDays` has three callers in UQM: the starbase's two weeks (built in 3a), the bomb's 14 days (`starbase.c`'s `DoTimePassage` or `InstallBombAtEarth`), and the Talking Pet's 15 (`genpet.c`'s `ZapToUrquanEncounter`: the ship moved to the Ur-Quan at (5288, 4892), its fuel charged for the way, the Ur-Quan's hypnosis talk). Each day runs its events, and a genocide can end the game mid-skip.

The port's only story pause is the starbase visit (`StarbaseVisit`, `GameWorld.StoryPaused`). A Pet jump or a bomb skip needs the pause generalised. During the starbase's two weeks the port rolls hyperspace encounters for every other player in hyperspace each day; UQM's lone player is never in hyperspace during a skip (question 3).

### QuasiSpace

Entered through the Arilou's monthly portal at hyperspace (438, 6372), which opens on the 17th for three days, or through a spawner's portal. QuasiSpace's coordinates overlap hyperspace's: the same map, a different place, told apart by each ship's `ArilouSpaceSide` (already each ship's own since 3b-2, always 0 now). In QuasiSpace no fuel burns, no encounters come, the broadcaster and the spawner don't work, the stars are the 15 vortices and the Arilou's home, and the screen is green (its colour map, star art and the status line's "QuasiSpace"). A vortex leads out to its own place in hyperspace; the 15 exit points (`hyper.h`, `QUASISPACE_PORTALS_HYPERSPACE_ENDPOINTS`) aren't in `data/` yet. The Arilou's home is a "star" that starts their talk (ported in 3b-2, tests only): always peaceful there, it starts the Umgah's zombies, and takes the portal key for the spawner.

What it needs: every check that takes "no star" for hyperspace (encounter rolls, groups, battles' places and reach, wrecks and rescue, fuel gifts, the snapshot, other ships drawn, star contact) compares the side too. UQM's QuasiSpace is flown with the star map and its radar; the port has neither (question 8).

### The Chmmr and the bomb

Freed by the Sun Device used in orbit of their world (after the shielded talk the broadcaster brings, or without it). Once they're freed, orbiting their world starts their talk; with the Talking Pet and the bomb aboard, their `proceed` makes the bomb ship (`chmmrc.c:131-232`): the Chmmr allied, 1,000,000 resource units, the landers taken and their price made 0, the ship's slots filled with the bomb's modules, its drive and jets replaced, and the ship moved to the starbase (or to Earth, without one); then 14 days. After it the starbase's talks change, outfitting locks the bomb's slots, Sol's groups are gone, and the bomb ship can't retreat in battle. The port's `ChmmrScript` rebuilds the answering ship (a guest's too) but doesn't move it or pass the days (row 87); the 14-day branch waits in `StarbaseVisit`, and `Outfitting.BombInstalled` and the bomb carrier exist from 3a and stage 4.

### Where the endgame starts

The endgame's, not 3c's: the Sa-Matra's system (`gensam.c`, its guards and talks), the Talking Pet used there (`READY_TO_CONFUSE_URQUAN`, `URQUAN_MESSED_UP`), the rebels' cavalry, the last battle (`IN_LAST_BATTLE`, `WON_LAST_BATTLE`, `ships/lastbat`), the victory's slides, the credits and the out-takes (the Utwig's `OUT_TAKES` among them). The genocide's wait while a player is at the Sa-Matra is 3c's (a system check only).

## Scope

**In stage 3c:**
- **The calendar:** all 15 events left, recorded against UQM; the set-aside events run (question 1); the location gates (question 2); the genocide's game over; each event a notice to everyone (3b-1, decision 11); `--start-date`.
- **The story worlds' pickups:** every node in the table above, with its report and the reports' entries on the wire; Fwiffo's crew loss and his talk.
- **Story pauses beyond the starbase**, and the Pet's and the bomb's skips (question 3).
- **The hyperspace and system menus**, each with Devices (question 4), and every device's use (questions 5 to 7).
- **QuasiSpace:** the monthly portal, the spawner's, flight there, the vortices and the way back, the Arilou at home; the 15 exit points extracted into `data/`.
- **The Talking Pet's world** (`genpet.c`'s Pet half): the zombies, the Pet's talk, the hypnosis and the jump.
- **The Chmmr:** the ways to them, freed, the bomb ship (question 9), its 14 days, the starbase and outfitting after the bomb.
- **Rows closed or changed:** 66 (set aside), 3 (the skips), 27 (the worlds that start a talk: the Pet's, the Chmmr's), 72 (the Yehat's rebellion check, `check_yehat_rebellion` in every system's `reinitNpcs`), 87 (the bomb ship), 36 (`PORTAL_COUNTER`).

**Not in stage 3c:**
- **The endgame** (above).
- **The star map** and the hyperspace radar (question 8).
- **The game menu's save and load from the hyperspace and system menus:** saving stays at the starbase as now (`porting-inventory.md`, the game menu row); the menus' other entries say they come later, as orbit's do.
- **Music:** the alternate songs and `QUASISPACE_MUSIC` wait for the audio stage.

## Co-op rules

Decided already:
- **Story flags are shared, and so is the group inventory.** Whoever's lander picks up the Maidens, the group has them; any player's Devices list shows them (`design.md`; row 10).
- **A talk is the answerer's ship's**; ships a talk gives go to the host's fleet, whoever answers (2026-11-12, 2026-11-15).
- **A time skip is a story pause for everyone, its fade on every screen** (`stage-3.md`, the story pause decision; row 3).
- **A pickup's report** goes to the player whose lander found it, which takes off; other landers on that world stay and are told (`stage-3.md`, decision 9; row 38).
- **Notices to everyone** (the owner, 2026-11-21): every event that changes a race (an alliance, a death, a shield, a fleet on the move) is told to every player.
- **The host's flagship lost is the game over for everyone** (row 50); the genocide's game over is everyone's too.

New in this plan (each with its question below; the rule written as recommended):
- **Set-aside events run in date order the first time 3c's code runs a game, from that day** (question 1).
- **A location gate waits while any player is in that system** (question 2).
- **No hyperspace encounters during a time skip** (question 3).
- **Devices:** any player uses them from their own menus, wherever they are; what a device does happens where its user is, and a talk it starts is the user's (question 5). The Utwig bomb destroys its user's ship (question 6). A spawner's portal is its user's, open to anyone who reaches it before it closes (question 7).
- **The bomb ship is the host's flagship, whoever answers the Chmmr** (question 9).
- **Report places:** each ship in orbit keeps its own place in a world's report text, as UQM's one visit does; where the text depends on what's been found (the artifact ruins, the Androsynth's chapters), it's read from the shared story when the node is touched, so two players never get the same chapter as new.
- **Fwiffo:** the crew is the finder's lander's; his talk starts when that lander is back aboard, under the Spathi's lock (it waits if another player holds it); he joins the host's fleet.
- **The Talking Pet's jump moves only the hypnotised player's ship**; the 15 days are everyone's.

## Work, in order

Each item is ported, recorded from UQM where UQM has the code, and reviewed as 3b-2's were. Each carries its own wire, view, save and doc pieces.

1. **The calendar's engine and the simple events.**
   - `RunEvent` widened; the set-aside events run (question 1); the location gates (question 2); the events run inside talks with their notices, and the opening's take-back covering what they change.
   - The events with no mission: the Arilou's entrance and exit (the story field and its counter; the portal itself is item 9's), the Umgah check, the Shofixti's return, the Spathi's shield, the Zoq-Fot-Pik's distress (with the Kohr-Ah's ship at their world) and death, the Yehat rebels with the rebellion check (row 72).
   - `--start-date`, so a test or a try-out can stand the clock where an event falls.
   - `clock.c`'s recorder extended to each event.
2. **The fleet missions.** The Pkunk's, the Thraddash's, the Ilwrath's, the Mycon's and the Utwig-Supox's, each over its whole course and recorded day by day. The status panel's fleet lines (question 8).
3. **The Kohr-Ah.** The victory, the genocide race by race, the frenzy, and the game over at Sol with the Black Ur-Quan's last talk shown to everyone; the gate at the Sa-Matra.
4. **Reports and the artifact pickups.** Reports' entries and places on the wire and the client; reports without a take-off; the surface sheets; the nodes on the scan following the story. The single-node pickups: the Maidens, the beast, the two broadcasters, the bomb, the Taalo protector, the Helix, the Sun Device, the egg cases.
5. **The ruins, the wreck and the vault.** The homeworlds' ruins, the three artifact ruins, the Androsynth's chapters, the Ur-Quan wreck and its portal key, the Syreen's vault, Procyon's starbase report.
6. **Fwiffo on Pluto.** The crew loss, his ship and talk, his joining the host's fleet.
7. **Story pauses generalised.** `StarbaseVisit`'s pause made a story pause any sequence can take: its fade, its skipped days, its take-back where one is needed, and no encounters meanwhile (question 3). The starbase's two weeks moved onto it unchanged.
8. **The menus and the devices.** The hyperspace and system menus (question 4) with Devices; every device's use (questions 5 and 6): the Ultron mended, the broadcasters' talks and calls, the Sun Device's flash (the Chmmr's half in item 11), the Utwig bomb. The Pet's in item 10, the spawner's in item 9.
9. **QuasiSpace.** The 15 exit points extracted; the monthly portal opening and closing on the screen; the spawner's portal (question 7); the crossing and its spin; flight in QuasiSpace with its rules and look; the vortices and the way back; the Arilou at home; every "in hyperspace" check made to compare the side; `Gift.CloseBy` and the snapshot (the handoff's open item).
10. **The Talking Pet's world.** The Umgah's world by `genpet.c`: the zombies and the Pet's talk, the Pet taken aboard, the hypnosis and the 15-day jump on item 7's pause, the Ur-Quan's hypnosis talk; the Pet used as a device outside the Sa-Matra.
11. **The Chmmr.** The shielded talk in orbit, the Sun Device freeing them, their world once freed, `proceed` on the host's flagship (question 9) with the move and the 14 days (or `InstallBombAtEarth`), the starbase, outfitting and Sol after the bomb.
12. **Docs.** The how-to-try for the stage, `design.md`, `porting-inventory.md`, `differences.md` (the rows above), `uqm-data.md` (the exit points), and what's left for the endgame gathered as 3b-2's "Left for 3c" was.

## Progress

| Item | State | Notes |
| --- | --- | --- |
| 1 to 12 | Not started | Waiting on the owner's answers |

## Risks

- **Size.** Twelve items, several touching everything: the calendar changes what every player meets, QuasiSpace touches every place check, and the menus are new screens. 3b-2's items took two to five review rounds each.
- **Long courses.** The missions and the genocide run over months and years of game time. Each is recorded day by day from UQM's own `gameev.c`, with `--start-date` and `--start-state` setting them up; a try-out by hand takes days of real time at the clock's rate, so the guide leans on the flags.
- **The set-aside events.** Run late, their re-queues count from the day they run (question 1). Games saved before 3c are test leftovers (the owner doesn't run the game), so this matters little in play, but it has to be safe: no event lost, none run twice.
- **QuasiSpace's overlap.** Every place check that forgets the side lets a ship in QuasiSpace meet, fight, rescue or be given fuel by a ship in hyperspace. Item 9 finds them all (the research listed nine) and tests each.
- **The save and the wire.** New per-ship state (the portal, the report places, the side in QuasiSpace) and a new story pause: the save's version and the protocol go up.
- **Presentation.** QuasiSpace's look, the portal's growth and spin, the flashes, the menus and the reports' entries are view work beyond 3b-2's.

## Questions

Each question has a recommendation (first).

1. **The events set aside since 3a.** Every 3c event a game reached before 3c sits aside with its date. UQM never has an event in the past, so there's no UQM answer.
   - **Run them in date order the first time 3c's code runs that game, from that day (recommended).** Their own re-queues count from then: a Zoq-Fot-Pik distress run late gives them six months from then, not from the distress's own date. Simple, and nothing is lost. Games from before 3c are test leftovers anyway.
   - Replay each on its own date: put the clock back, run the calendar forward day by day to today, fleets and all. Faithful, but a second clock running over a live world, for saves nobody keeps.
   - Refuse saves with set-aside events: the simplest, but the 3b saves are lost.
2. **Location gates with several players.** UQM holds the Spathi's shield, the Zoq-Fot-Pik's distress and death, and the genocide (at the Sa-Matra) for 7 days at a time while the player is in that system, so the change never happens under them.
   - **While any player is in that system, flying, orbiting or landed (recommended).** Nobody sees it happen under them. A player who stays there holds the event, as in UQM.
   - Only the host: the event can happen under a guest.
   - No gates: simpler, but a player can watch a fleet vanish while they're talking to it.
3. **Hyperspace encounters during a time skip.** The port's starbase skip rolls each skipped day for every other player who is in hyperspace; their ships are frozen meanwhile, so a group made then waits beside a frozen ship. UQM's player is never in hyperspace during a skip.
   - **No rolls during a skip (recommended)**; the fleets still move and every event still runs.
   - Roll as now.
4. **The hyperspace and system menus.** The port has neither; UQM's hold the star map, Devices, Cargo, the Roster and the game menu. Devices needs a way in from hyperspace and from a system.
   - **Both menus as UQM's, with Devices, Cargo and the Roster working (they exist from orbit), the star map and the game menu saying they come later (recommended).**
   - Only a key that opens the Devices list anywhere: less work, unlike UQM.
5. **Who may use a device, and must the group be there** (`design.md`'s open item, deferred since the survey).
   - **Any player, from their own menus, wherever they are; what it does happens where its user is, and any talk it starts is its user's (recommended).** The device isn't used up for others unless UQM uses it up (the Ultron's pieces are; the Sun Device and the broadcasters aren't). The group needn't be there: a device's effects are UQM's single-ship ones, done at the user's place.
   - The host only: simple, but a guest at Procyon with the Sun Device couldn't free the Chmmr.
   - Any player, but only with every player in the same system: closest to "the group uses it", but it stalls a group spread out.
6. **The Utwig bomb set off.** In UQM it destroys the flagship, which ends the game. The bomb is the group's (it's also what the Chmmr need), so any player could set it off.
   - **It destroys its user's ship (recommended):** the host's flagship is the game over for everyone, as UQM's; a guest's ship becomes a wreck for a friend to revive, as a guest surrendering to the Ur-Quan does (row 73). Either way the bomb is gone.
   - The game over for everyone, whoever sets it off.
   - Only the host may set it off.
7. **The spawner's portal with several players.** UQM's portal opens at the ship, grows, and stays open until the ship goes through; while it's open, no star can be entered.
   - **Each ship's own (recommended):** `PORTAL_COUNTER` and the portal's place become the user's; only the user is kept from stars. Anyone who reaches the portal goes through while it's open; it closes a few seconds (a setting, 5 to start) after its user has gone through or as its user leaves hyperspace. The 10 fuel units are the user's.
   - Each ship's own, and only its user can go through.
   - One portal for the group, as UQM's shared field: one player's spawner opens it under every ship in hyperspace, and blocks all of them from stars.
8. **The star map.** UQM's hyperspace and QuasiSpace are flown with the star map and the radar; the port has neither, and the calendar's fleets moving can't be seen without them.
   - **Not in 3c (recommended): a stage of its own, with the radar and autopilot.** 3c adds the status panel's lines for the fleets the player knows (`KnownStrength` and `KnownLoc`, as UQM's star map would show them) and the 16 QuasiSpace stars to the coordinates the panel already shows.
   - In 3c, as a thirteenth item: the map, its zoom, search and autopilot, other players' places, the spheres. About an item's size, likely two.
9. **Whose ship carries the bomb** (3b-2's question 8, left for 3c). The Chmmr's `proceed` rebuilds one ship as the bomb ship (its slots, drive and jets, its landers taken), gives it 1,000,000 RU, moves it to the starbase and runs 14 days; the last battle needs the bomb aboard.
   - **The host's flagship, whoever answers (recommended).** It's the ship whose loss ends the game, as the bomb ship's is in UQM, and the endgame's battle builds on it. A guest who answers the Chmmr sends the host's flagship to Earth, as a guest's talk already gives to the host's fleet. The resource units go to the host. Everyone else keeps their place through the 14 days.
   - The answering ship, as built now: a guest's ship could become the bomb ship, and the endgame would have to take that ship to the Sa-Matra.
10. **The order of items.**
    - **As above (recommended):** the calendar first, since it moves the story everyone plays; then the pickups, since they need nothing new; then the pause, the menus and devices, and the three places that use them all.
    - QuasiSpace or the Chmmr first, to reach the most-wanted places sooner; the calendar after.
