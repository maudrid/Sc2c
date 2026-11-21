# Stage 3b-2 plan: the races

**Status:** draft, 2026-11-21, for the owner's review. Nothing in it is built. The open questions are at the end, each with a recommendation; the plan as written follows the recommendations.

Build order item: the second part of stage 3b (`docs/design.md`; `docs/plans/stage-3b.md`, decision 1). 3b-1 built the engine: the clock's events, the fleets, hyperspace encounters, system groups, the meeting (encounter screen, talk, battle, battle-won screen), notices and five scripts. 3b-2 is mostly scripts on that engine: the other 20 conversations, the worlds that start them, what they do to the game, and the Melnorme's trading.

Stage 3a's, stage 4's, stage 4b's and 3b-1's decisions hold here unless a question below changes one. Two things the approved stage 3b plan gave to 3b-2 move to 3c here: the Talking Pet's world (its zombies, hypnosis and jump need QuasiSpace and a time skip; item 10) and the Kohr-Ah's ship at the Zoq-Fot-Pik's world (it comes with their distress, a 3c event). Terms are 3b-1's (`docs/plans/stage-3b.md`): a hyperspace encounter, a system group, meeting a group.

## Goal

1. **Every race talks.** Each of the 20 scripts left is ported whole and checked against UQM, path for path, as 3b-1's five were. Every race's ships fly in hyperspace and systems by UQM's rules, and the stand-in meeting for races that aren't ported goes (`differences.md` row 68 closed).
2. **The worlds that start a talk** (row 27): the homeworlds, Spathiwa's moon, the Slylandro gas giant, the Syreen's starbase, and the guarded worlds (the Taalo protector's moon, the Maidens' world, the bomb's moon, the Sun Device world, the Aqua Helix world). Each starts its talk as UQM's does, with an infinite fleet or its finite guard.
3. **What talks do.** Alliances made and broken, which change the shipyard. Ships given to and taken from the host's fleet. The Druuge's trade in crew. Each one comes with a notice to the other players (3b-1, decision 11).
4. **The Melnorme.** Credits for each player, the trading (fuel, technology, information, bio data and rainbow worlds), the rescue of a ship out of fuel, and their groups in the nine super giants.
5. **The special systems** (the 3b-2 half of row 72):
   - Tanaka's scout (or Katana's), whose battle ship is the damaged scout left by stage 4b item 4;
   - his death marked when his group is gone;
   - the recruited Shofixti and the Zoq-Fot-Pik scout flying off after a meeting.

### What the owner will be able to try when it's done

- Fly into any race's sphere and meet its ships: each race talks and fights as in UQM. `--start-encounter SHIP` (a ship id from `data/ships.json`) brings a group of any race with a talk in space. From item 11 it refuses a race UQM gives no talk in space (`RACE_COMMUNICATION`'s `INVALID_CONVERSATION`: the Syreen, the humans), with a message. A Shofixti group runs Tanaka's talk, and a Chmmr group the Chmmr's, as UQM would.
- Orbit a race's homeworld: its talk, against its infinite fleet if it comes to a fight. Orbit Spathiwa's moon, the Slylandro gas giant and the Syreen's starbase: their talks.
- Make an alliance (the Orz, the Zoq-Fot-Pik, the Spathi with the password, the Thraddash after 25 of their ships destroyed): the race's ships go on sale at the starbase. Win the Pkunk's friendship: they aren't allied in UQM (`pkunkc.c` never calls `SetRaceAllied`), but they give 4 ships, and more each year. Gifts of ships arrive in the host's fleet. Every player is told.
- Run out of fuel in hyperspace: the Melnorme may come, and they strip the ship in exchange for fuel to get home. Fly into a super giant's system: a Melnorme ship is there. Trade with them:
  - sell bio data and rainbow worlds for credits;
  - buy fuel, lander upgrades, modules and information;
  - another player can top up the trader's credits mid-talk.
- Meet Tanaka three times to win him over. Meet the Zoq-Fot-Pik scout.
- Reach the branches that need 3c's devices or events with testing flags (`--start-devices`, and the new `--start-state`, item 1). The Talking Pet's talk is the one script with no way in until 3c: its world is 3c's (item 10), so only the tests run it.

## What the research found

Five research passes over UQM's source and the port (2026-11-21) found three things that shape this plan.

### Much of 3b-2's content needs 3c

Many branches of the 20 scripts open only through a device used, a story world's energy node picked up, or a 3c event run. 3b-1 met the same thing in the Ur-Quan's script (the Sa-Matra's branches, the hypnotised captain), ported it whole, and recorded every path from set-up states. This plan does the same (question 1): every script whole and recorded, every entry that play can reach wired, and the rest reachable through testing flags until 3c.

| Script | C lines | Met by, in 3b-2 | Reachable in play before 3c | Needs 3c or the endgame |
| --- | --- | --- | --- | --- |
| Arilou (`arilouc.c`) | 855 | Hyperspace only (20%, no system groups) | The talk in space (peace unless attacked) | The home talk in QuasiSpace; `ARILOU_UMGAH_CHECK`; the portal key for the spawner |
| Chmmr (`chmmrc.c`) | 641 | Nothing: no sphere | None | Every entry (the broadcaster in orbit, the Sun Device, the Chmmr unleashed); the bomb ship's rebuild and its 14 days |
| Druuge (`druugec.c`) | 926 | Sphere (30% / 20%); homeworld | Buying ships, fuel and artifacts for crew; the salvage fight; their anger | Goods to sell (Maidens, egg cases, broadcasters, spawner); the bomb's moon guard (5 ships, only once the Utwig have their Ultron back); the bomb's pickup |
| Mycon (`myconc.c`) | 643 | Sphere (40% / 20%); homeworld; the Sun Device world | Space, homeworld and Sun Device world talks (nearly always a fight; the Sun Device world's guard is infinite until the ambush) | The trick (`MYCON_AMBUSH` from the Syreen) and its mission event |
| Orz (`orzc.c`) | 898 | Sphere (20% / 20%); homeworld; the Taalo protector's moon (14 ships) | All of it: the alliance, the break over the Androsynth | The protector's pickup |
| Pkunk (`pkunkc.c`) | 1,148 | Sphere (40% / 20%); homeworld | Their friendship (`PKUNK_MANNER` 3, not an alliance: their ships never go on sale), 4 ships and the yearly gift, the Clear Spindle given | The migration (`ADVANCE_PKUNK_MISSION`) |
| Yehat rebels (`rebel.c`) | 449 | Nothing until `YEHAT_REBEL_EVENT` | None | The revolt's event; the endgame's cavalry |
| Shofixti (`shofixt.c`) | 652 | Tanaka's scout (a special group) | The three meetings to his friendship | The Maidens to recruit him; the return event (the alliance) |
| Slylandro at home (`slyhome.c`) | 921 | The gas giant (no fleet) | All of it: the self-destruct code | None |
| Spathi at home (`spahome.c`) | 1,018 | Spathiwa's moon (infinite fleet) | The hostile talk; with the password (from the Melnorme), the quest and the alliance | Fwiffo; the shield event (the Evil Ones' count comes into 3b-2: question 2) |
| Supox (`supoxc.c`) | 708 | Sphere (40% / 20%); homeworld | The neutral and hostile talks; the broken Ultron handed over | The alliance (by the Utwig's repaired Ultron), their mission, their ships |
| Syreen (`syreenc.c`) | 878 | Their starbase (no fleet; never met in space) | The first talks | The shuttle, the vault, egg cases, the ambush, the alliance |
| Talking Pet (`talkpet.c`) | 841 | Nothing until 3c (tests only; in UQM, the Umgah's world once the Umgah are zombies or the Kohr-Ah have destroyed them) | None | The zombies (the Arilou in QuasiSpace), the Umgah's end, the devices, the jump to the Ur-Quan, the Sa-Matra |
| Thraddash (`thraddc.c`) | 954 | Sphere (60% / 20%); homeworld; the Aqua Helix guard | The hostile talks; the alliance after 25 of their ships destroyed | The mission event; the Helix's pickup; the Ilwrath's war |
| Umgah (`umgahc.c`) | 729 | Sphere (30% / 20%); homeworld | The Umgah before the Pet (every exit a fight) | The zombies and the reward |
| Utwig (`utwigc.c`) | 996 | Sphere (40% / 20%); homeworld; the bomb's moon | The neutral and hostile talks, the bomb's guard | The Ultron's repairs (devices), the mission, their ships |
| VUX (`vuxc.c`) | 796 | Sphere (40% / 20%); homeworld; Admiral ZEX at the Maidens' world | Every VUX talk (all fights); ZEX without the beast | The beast and the Maidens |
| Yehat (`yehatc.c`) | 685 | Sphere (60% / 40%); homeworld | The talks, the Shofixti mentioned once Tanaka has been met | The revolt (`ok_send` needs a Shofixti ship in the fleet, which needs the Shofixti's return; before 3c only `--start-fleet` reaches it) and its event |
| Zoq-Fot-Pik (`zoqfotc.c`) | 975 | Their scout (a special group); homeworld (0% elsewhere) | The alliance | Distress, death and rescue (events); the Kohr-Ah at their world |
| Melnorme (`melnorm.c`) | 1,855 | Hyperspace (30% a day with no fuel); the nine super giants | All of it | The broadcaster's 100% (a device) |

### What scripts call that the port lacks

The port's `Talk` covers 3b-1's scripts. The 20 left also call:
- **Alliances:**
  - `SetRaceAllied` (Chmmr, Orz, Spathi at home, Syreen, Thraddash, Utwig, Zoq-Fot-Pik);
  - `RemoveEscortShips` (Orz);
  - `StartSphereTracking` from a script (Supox, Talking Pet, Utwig).
- **The fleet:** `HaveEscortShip` (Spathi at home, Yehat); the escort queue read through `GetHeadLink` (the Druuge's salvage check; the Talking Pet's `GetHeadLink` reads the enemy's ships, which a talk already has).
- **The ship:** the Umgah's reward adds 500 bio data to the answering ship (`ITalkShip.BioData` is read-only now).
- **The talk's own fields:** the Arilou read `ARILOU_SPACE_SIDE` (in QuasiSpace or not), which the port keeps out of the story (`StoryState.NotShared`); the talk must be given it as its own field, the answering player's.
- **Speakers:** `NPCPhrase_cb`, a callback per phrase (Zoq-Fot-Pik: every line switches between Zoq and Pik, with their own frames, text place and colours).
- **The Melnorme's own:**
  - credits;
  - `ShipWorth`;
  - `StripShip`, with its own random context seeded from the ship's place and kit;
  - the cargo display counting bio data down;
  - the status line showing credits.

The recorder (`tools/extract/golden/comm.c`) needs stubs for each.

Outside the scripts:
- **The Thraddash's body count** (`encount.c:762`, in `UninitEncounter`): their ships destroyed in battles with them while `THRADD_MANNER` isn't set (not even after 3c's event breaks an alliance). The port lacks it, and it's the only way to their alliance.
- **The world talks:**
  - bit 6 talks (a race's second world);
  - finite guards that set a flag when none is left (the Druuge's 5 at the bomb, the Orz's 14 at the Taalo moon, the Mycon's 5 at the Sun Device once ambushed);
  - talks with no fleet (the Slylandro gas giant and the Syreen's starbase, now `NotEntered`);
  - a talk before a talk (the Kohr-Ah ship at the Zoq-Fot-Pik's world in distress, 3c).

`GLOBAL_FLAGS_AND_DATA` is already each talk's own in the port (`StoryState.NotShared`), so a world's bit 7 can't leak into a later meeting, as UQM's clearing after every talk (`comm.c:1423`) ensures.

### Surprises worth knowing

- **Talks that end without a fight:**
  - The Supox never fight from a reply; only the encounter screen's Attack fights them.
  - The Slylandro at home, the Syreen and the Chmmr never fight.
  - The Melnorme, the Shofixti, the Syreen, the rebels, the Slylandro at home, the Chmmr and the Talking Pet start at peace: no encounter screen.
- **Odd endings:**
  - The Umgah's reward gives 4 ships and then fights at an infinite fleet.
  - The Utwig's `real_sorry_about_ultron` ends in a battle, as init's hostile segue is left standing.
  - The recordings will settle both.
- **The rebels give royalist Yehat ships,** not rebel ones (`rebel.c:341`).
- **Who is never met as random groups:**
  - The Zoq-Fot-Pik have 0% in hyperspace and systems, even at home; their homeworld doesn't call `NotifyOthers`.
  - The Syreen and the Shofixti have no fleet.
  - The Melnorme come only by their special rules.
- **The Mycon and the Yehat draw `TFB_Random` mid-talk:** the talk's own generator (stage 3, decision 12) covers it.
- **Tanaka's friendship takes two battles he survives first** (the players retreat), then a third meeting.

## Scope

**In stage 3b-2:**
- **The 20 scripts, whole,** each recorded from UQM and matched path for path. Their rules in `TalkRules` are all ordinary (3b-1, decision 6), except the Chmmr's bomb branch, which 3c sets when it comes.
- **What talks call** (above), with the recorder's stubs. Alliances change the shipyard (`Outfitting.BuildList` already follows the standing). Ships given and taken use stage 4b's fleet functions. Each change is a notice.
- **Every race's groups fly** (`GameWorld.TalkingRaces` becomes every race), as each item ports its races. The stand-in meeting goes with the last race.
- **The worlds that start a talk.** For every 3b-2 race, its world and its guarded worlds, by their `gen*.c`; the Slylandro gas giant and the Syreen's starbase entered and talked at.
- **The Thraddash's body count.**
- **The Melnorme:**
  - credits per player (`differences.md` row 19, decided in `design.md`): kept in the player's hold, saved, sent, shown in the status line during their talk, and given between players at any time, mid-talk too (`World/Trading.cs`);
  - the script with its trading;
  - the rescue's strip;
  - their nine super-giant groups flying;
  - the rainbow worlds marked on orbit (`genrain.c`, question 2).
- **The broadcaster's use becomes its user's own** (already required by `stage-3b.md`'s 3b-2 row: the port's roll clears `USED_BROADCASTER` after each player's roll, so the first player in order would get the Melnorme whoever used it). The device itself comes with 3c; `--start-state USED_BROADCASTER=1` sets it for the host until then.
- **Tanaka's scout:**
  - the damaged ship (`oldscout`/`oldcaptain` sheets, weapon wait 10, the limpets' slowness; left by stage 4b item 4);
  - his death marked (`GenerateShofixti_uninitNpcs`);
  - the recruited Shofixti flying off (`check_old_shofixti`).
- **The Zoq-Fot-Pik scout** flying off after its meeting.
- **Wire, views and saves:**
  - credits in the snapshot and the save;
  - the Zoq-Fot-Pik's two speakers on the comm screen;
  - the Melnorme's bio data counted down;
  - the alternate songs noted for the audio stage;
  - the save's version goes up once (14), for each player's credits, refused rescue and broadcaster flag; the protocol goes up.
- **`--start-state NAME=VALUE`** (testing, new): set story fields at the start, to reach branches that wait on 3c.

**Left out of 3b-2:**

| Left out | Why | Goes to |
| --- | --- | --- |
| The events 3b-2's talks queue: the Yehat's revolt, the Shofixti's return, the Zoq-Fot-Pik's distress and death, the Spathi's shield, the Mycon's, Pkunk's, Thraddash's (and the Ilwrath's that follows it) and Utwig-Supox missions, the Arilou's Umgah check | The event calendar is 3c's; they're set aside as they fall due (3b-1, decision 9; question 3) | 3c |
| Story worlds' pickups that are devices: the Maidens, the VUX beast, the Aqua Helix, the Ultron's parts, the Sun Device, the egg cases, the Clear Spindle's use, the Taalo protector, the bomb, the portal key, the broadcasters | Devices and energy nodes are 3c's | 3c |
| The broadcaster's and the devices' talks (the Chmmr shielded, the Ilwrath's gods, the Talking Pet compelling) | Device use | 3c |
| The Chmmr's bomb ship: the rebuild, the 14 days, whose ship it is | Needs the bomb, the Pet and the time skip; a co-op decision of its own (question 8) | 3c or the endgame |
| The Talking Pet's world (`genpet.c`'s Pet half): the zombies with the Pet's talk, the hypnosis and the jump to the Ur-Quan (`ZapToUrquanEncounter`: 15 days, the answerer moved), the Pet once the Umgah are gone | The zombies need QuasiSpace, the jump is a time skip, the Umgah's end is the Kohr-Ah's genocide | 3c |
| QuasiSpace and the Arilou at home | The portal | 3c |
| The Yehat rebellion check (`check_yehat_rebellion`), the Sa-Matra's guards and talks, the rebels' cavalry | Story and the endgame | 3c, the endgame |
| The alternate songs (`SPAHOME_MUSIC`, `REBEL_MUSIC`, `UTWIG_ULTRON_MUSIC`) | Nothing plays music yet | The audio stage |

## Co-op rules

Decided already:
- **A talk is the answerer's ship's:** its names, hold, crew and fuel. Ships a talk gives go to the host's fleet, whoever answers.
- **Story flags are shared,** and so are a race's standing (3b-1, decision 8) and the group inventory of key items (`design.md`). The Maidens, the beast, the Ultron, the shuttle, the Pet and the spindle are the group's, wherever they were picked up, as `--start-devices` already treats them.
- **One player per race in an ordinary talk** (the dialogue lock). The rebels lock with the Yehat, the Talking Pet with the Umgah, the Slylandro at home with their probes, as `RACE_SHIP_FOR_COMM` maps them.
- **Melnorme credits** are one balance per player, giftable at any time (`design.md`; row 19).
- **With one player, everything plays as UQM's.**

New in this plan (each an open question below, written here as recommended):
- **Ships in the fleet.** `HaveEscortShip`, `EscortFeasibilityStudy` and `RemoveEscortShips` read and change the host's fleet, where gifts go (question 4).
- **Melnorme technology.** A module bought from the Melnorme goes on sale for everyone, as the shared module prices already do. A lander upgrade is the answering ship's. The catalog offers the first item the answering ship lacks (question 5).
- **Rainbow worlds and the Melnorme's anger** stay shared, as story flags are: a world is paid for once, to whoever sells it first; one player angering the Melnorme angers them with everyone (question 6 asks the owner to confirm).
- **The Melnorme's rescue.** A refused rescue is that player's own; a rescuer that touches a player while another player trades with the Melnorme follows them until that talk ends (question 7).
- **The Thraddash's body count** counts every player's kills in a battle with them (question 9).

## Work, in order

Each item is ported, recorded from UQM and reviewed as 3b-1's items were. Each item carries its own wire, view and save pieces. Each race's groups start flying in the item that ports its talk.

1. **What talks can do.**
   - `Talk` gains `SetRaceAllied` (a dead race stays dead), `RemoveEscortShips`, `StartSphereTracking`, `HaveEscortShip`, the escort queue read in order, and `NPCPhrase_cb`.
   - Each gets the recorder's stub and a notice event: an alliance made or broken, ships given or taken.
   - The world talks generalised: bit 6 worlds, finite guards with their "none left" flags, talks with no fleet.
   - The answering ship's bio data writable by a talk (the Umgah's reward). `ARILOU_SPACE_SIDE` given to a talk as the answering player's own field (whether they're in QuasiSpace).
   - The Thraddash's body count in the battle's end.
   - `--start-state`.
   - One golden file per script, so `talks.json` doesn't grow to tens of megabytes (risk below).
   - Checked against UQM where UQM has the code (`SetRaceAllied`'s cases, the body count's `UninitEncounter`).
2. **The Spathi and the Slylandro at home.**
   - `spahome.c`: Spathiwa's moon with its infinite fleet (row 74 closed), and the password, quest and alliance reached with `--start-state`.
   - `slyhome.c`: the gas giant entered and talked at, and the self-destruct code and the ramp-down it sets.
   - The Evil Ones' count on Spathiwa's moon (question 2).
3. **Melnorme credits.**
   - Each player's balance: the hold, the save (version up), the snapshot, the status line's credits mode, gifts between players (`Trading`), mid-talk too.
   - The rainbow worlds marked on orbit (`genrain.c`).
   - No talk and no Melnorme ships yet (3b-1, decision 2: a race's ships appear once its talk is ported).
4. **The Melnorme.** `melnorm.c` whole, recorded over a matrix of states (anger, fuel, bio data, credits, new rainbow worlds, the catalog's place, the information stacks, the strip):
   - the first meeting and the bluster;
   - trading: fuel by the ship's own tanks, the technology catalog, information with its story flags;
   - selling bio data (the count-down on the cargo display, skipped by a key) and rainbow worlds;
   - anger and its battles;
   - the rescue's `StripShip` with its own seeded generator, measured to Sol from the answering ship, its worth and strip read from that ship's own slots (a guest's escort: its kit's pieces, its capped tanks, its kit crew; stage 4 item 8's note); `MELNORME_RESCUE_REFUSED` made each player's own (question 7);
   - `ok_strip_me`: a whole ship stripped to calm their anger;
   - the out-of-fuel rule meeting the talk, and the broadcaster flag made its user's;
   - their nine super-giant groups flying (row 68's special groups).

   It's the largest item, about twice a 3b-1 script item.
5. **The Shofixti and the Zoq-Fot-Pik.**
   - `shofixt.c`, with Tanaka's scout:
     - the damaged ship's sheets and stats;
     - his three meetings, the two battles he survives first;
     - his death marked when a player leaves the system with his group gone;
     - Katana after him (he comes only with the Maidens aboard, a 3c pickup: reached by `--start-devices`);
     - the recruited Shofixti flying off.
   - `zoqfotc.c`:
     - the two speakers through `NPCPhrase_cb`, on the comm screen too;
     - the scout's group and its flight after the meeting;
     - the homeworld without `NotifyOthers`;
     - the alliance.
   - Row 72's 3b-2 half closed.
6. **The VUX and the Yehat.**
   - `vuxc.c`: the homeworld; ZEX at the Maidens' world; the beast's branch by `--start-devices`.
   - `yehatc.c`: the homeworld; the revolt's offer while a Shofixti ship is in the host's fleet; its event set aside.
   - `rebel.c`: whole, recorded, reached only with `--start-state` until 3c runs the revolt.
7. **The Orz and the Pkunk.**
   - `orzc.c`: the homeworld, the alliance and its break (`RemoveEscortShips`), and the Taalo protector's moon with its 14 guards.
   - `pkunkc.c`: the homeworld, their friendship (no alliance in UQM), the 4 ships and the yearly gift by date, the Clear Spindle given.
8. **The Druuge and the Mycon.**
   - `druugec.c`: the homeworld's trade in the answering ship's crew (ships to the host's fleet, fuel by the ship's tanks, artifacts to the group), the crew price rising at the starbase, and the salvage fight.
   - `myconc.c`: the homeworld, the Sun Device world's guard, and the mid-talk draw.
9. **The Utwig and the Supox.**
   - `utwigc.c`: the homeworld; the bomb's moon guarded by the Utwig, then by the Druuge (item 8's script) once the Utwig have their Ultron, reached by set-up until 3c; the Ultron's branches by `--start-devices` and `--start-state`.
   - `supoxc.c`: the homeworld, and the Ultron handed over.
10. **The Thraddash, the Umgah and the Talking Pet.**
    - `thraddc.c`: the homeworld, the Aqua Helix guard, and the alliance by the body count.
    - `umgahc.c`: the Umgah's world (`genpet.c`'s Umgah half).
    - `talkpet.c`: whole and recorded; reached only by tests until 3c. `genpet.c`'s Pet half goes to 3c whole: the zombies come with the Pet's own talk, which hypnotises a player without the Taalo protector (`PLAYER_HYPNOTIZED`) and jumps them to the Ur-Quan with 15 days gone (`ZapToUrquanEncounter`, a time skip); wiring it without the jump would leave the shared flag set for the next Ur-Quan meeting. The Pet's talk once the Umgah are gone needs the Kohr-Ah's genocide (3c) too.
11. **The Arilou, the Syreen and the Chmmr.**
    - `arilouc.c`: their meetings in hyperspace (the answering player's `ARILOU_SPACE_SIDE`), and the home talk recorded.
    - `syreenc.c`: their starbase entered and talked at.
    - `chmmrc.c`: whole, recorded, reached only by set-up (question 8).
    - Procyon's Ilwrath (`genchmmr.c`): orbiting the Chmmr's world before they're unleashed, with the Sun Device aboard, while the Ilwrath live and aren't deceived, builds their infinite fleet and starts the Ilwrath's talk at bit 6, the branch 3b-1 ported. Wired here, reached by `--start-devices` until 3c (row 27).
    - With this item every race talks: `TalkingRaces` is every race, and the stand-in meeting and row 68 go. `--start-encounter` refuses a race with no talk in space (the Syreen, the humans).
12. **Docs.**
    - `docs/how-to-try/stage-3b-2.md`.
    - `design.md`: the build order.
    - `porting-inventory.md`.
    - `differences.md`: rows 19, 27, 36 ("the credits come with 3b"), 68, 72 and 74, and new rows for this plan's co-op rules and the events set aside.
    - Code comments that call the Ilwrath's broadcaster branches 3b-2's (`IlwrathScript.cs`, `IlwrathSpathiMeetingTests.cs`): they're 3c's, with the device.
    - `uqm-data.md`.

## Progress

| Item | State | Where |
| --- | --- | --- |
| 1. What talks can do | Not started | |
| 2. The Spathi and the Slylandro at home | Not started | |
| 3. Melnorme credits | Not started | |
| 4. The Melnorme | Not started | |
| 5. The Shofixti and the Zoq-Fot-Pik | Not started | |
| 6. The VUX and the Yehat | Not started | |
| 7. The Orz and the Pkunk | Not started | |
| 8. The Druuge and the Mycon | Not started | |
| 9. The Utwig and the Supox | Not started | |
| 10. The Thraddash, the Umgah and the Talking Pet | Not started | |
| 11. The Arilou, the Syreen and the Chmmr | Not started | |
| 12. Docs | Not started | |

## Risks

- **Size.** About 17,600 lines of C in 20 scripts, in twelve items. 3b-1's script items took two to five review rounds each, and the Melnorme's is about twice one of them.
- **Much of it is only tested, not played, until 3c.** Branches behind devices and events are recorded and reached through testing flags. Until 3c runs, a player meets fewer of them, and the alliances made in 3b-2 don't move on: the Spathi never shield themselves, the Zoq-Fot-Pik are never in distress, and the Pkunk never migrate.
- **The recordings' size.** `tests/golden/talks.json` is 6.6 MB for seven scripts. Twenty more, the Melnorme's matrix the largest, would make one file of tens of megabytes. Item 1 splits it into one file per script.
- **The Melnorme's paths.** Their trading loops over fuel, credits, bio data, rainbow worlds, the catalog and anger, so walking every path needs bounded set-up states rather than one walk. The strip's own generator, and its quirk of reading the real ship's worth inside the copy's loop, must be matched exactly.
- **The save and the wire.** Credits are new state for each player. The save's version goes up (14), and an older save gets zero credits. The protocol goes up for credits and the two-speaker talk.
- **Presentation.** The Zoq-Fot-Pik's talk is the one script whose every line changes the screen, and the Melnorme's bio count-down is the one animated trade. Both need view work beyond 3b-1's comm screen.

## Open questions

Each has a recommendation (first) and the plan as written follows it. The owner's answers change the items they name.

1. **Branches that need 3c.** About a third of the 20 scripts' paths open only through 3c's devices or events (the table above).
   - **Port every script whole and record every path; wire every entry play can reach now; reach the rest with `--start-devices` and `--start-state` until 3c (recommended).** This is how 3b-1 did the Ur-Quan's Sa-Matra branches. 3c then only wires entries to scripts already checked. The Talking Pet's talk is the exception: it has no way in until 3c, so only the tests run it.
   - Port only what play can reach now, the rest with 3c: smaller items now, but 3c reopens every script and re-records it.
2. **Small story pickups that unlock talk paths.** Two pickups need no device and are read by 3b-2's talks:
   - the rainbow worlds marked on orbit (`genrain.c`), which the Melnorme buy;
   - the Evil Ones' count on Spathiwa's moon (`genspa.c` sets `SPATHI_CREATURES_EXAMINED`/`_ELIMINATED`; the port makes those life forms but sets neither flag), which the Spathi at home ask about.

   Options:
   - **Bring both into 3b-2 (recommended):** small, no devices, and without them two branches can't be reached honestly.
   - Leave both with 3c's story worlds: the Melnorme never buy a rainbow world, and the Spathi's quest can only be lied about.
3. **3c's events that 3b-2's talks queue.** These are the Yehat's revolt, the Shofixti's return, the Zoq-Fot-Pik's distress and death, the Spathi's shield, the Arilou's Umgah check, and the Mycon's, Pkunk's, Thraddash's and Utwig-Supox missions.
   - **Leave them all set aside until 3c runs the calendar (recommended), with a `differences.md` row:** alliances made now don't move on until 3c. Most of these events move fleets, kill races or change spheres, which is the calendar's work. Of the cheap ones, the revolt needs a Shofixti ship, which needs the Shofixti's return, which needs the Maidens (a 3c pickup), so bringing it forward gains nothing in play.
   - Bring the ones that only set flags or make a sphere (the revolt, the Shofixti's return, the Arilou's Umgah check) into 3b-2. They would run but stay hard to reach.
4. **Whose fleet a talk looks at.** Gifts and their room check already go to the host's fleet, whoever answers (2026-11-15; stage 4b, decision 3). New in 3b-2 are the talks that look at the escorts or take them back: the Yehat's revolt needs a Shofixti ship, the Spathi at home ask after Fwiffo, the Druuge see an unarmed ship to salvage, and the Orz take their ships back when the alliance breaks.
   - **The host's fleet for these too, whoever answers (recommended),** so what's given is what's checked and taken back. For the Druuge's salvage test, an unarmed guest is covered by the host's escorts.
   - The answering player's own escorts: a guest has none, so a guest could never offer the Yehat a Shofixti, and the Orz would take back ships from a fleet they weren't given to.
5. **The Melnorme's technology: confirm, and one new rule.** UQM's catalog sells 13 items in order, each once. Six are modules (the blaster, point defence, the high-efficiency fuel system, tracking, the cannon and the Shiva furnace). Buying one sets the module's shared price, so it goes on sale at the starbase for everyone, escorts included where an escort can hold it (escorts can't hold the fuel system). The other seven are lander upgrades. Both follow rules already approved: module prices have been shared since stages 3 and 4, and lander upgrades have been each ship's own since stage 2b (row 36). What's new is the catalog's order with several ships.
   - **The catalog offers the first item the answering ship lacks (recommended):** a module anyone has bought is skipped for everyone; a lander upgrade is skipped only for the ship that has it. Each player pays for their own lander upgrades.
   - The catalog walks the group's purchases: a lander upgrade one player bought is skipped for everyone, and others can't buy it.
6. **Rainbow worlds and the Melnorme's anger: confirm they stay shared.** Both are story fields, and story flags are shared (row 36), so these follow from rules already approved; the question is only whether the Melnorme should be an exception.
   - **Shared (recommended).** A rainbow world (500 credits) is found once for the group and paid for once, to whoever sells it first; the seller can give the credits on. Angering the Melnorme (up to `MELNORME_ANGER` 3, which also stops their coming to ships out of fuel) angers them with everyone, and every player is told. To calm them, any player can offer their ship to be stripped whole (`ok_strip_me`), whoever angered them.
   - Per player, as an exception: each player sells the worlds they orbited themselves (the group can be paid several times for one world), and each keeps their own standing with the Melnorme (the one race that would remember players apart).
7. **The Melnorme's rescue of a ship out of fuel.** UQM's Melnorme greet a ship that refused their rescue with "changed your mind?" (`MELNORME_RESCUE_REFUSED`). They're also one race under one dialogue lock: under 3b-1's decision 5, a group whose race is in another player's talk passes through a player it touches.
   - **A refused rescue is that player's own (a field kept out of the story, as the credits are); a rescuer that touches a stranded player while another player is talking to the Melnorme passes through, keeps following them, and meets them once that talk is over (recommended):** the lock as decided, and nobody is left stranded by another's long trade.
   - The refusal shared, as UQM's: a second stranded player is greeted as if they had refused.
   - The rescue exempt from the lock: two Melnorme talks at once, against decision 5.
8. **The Chmmr.** Every way to them is 3c's: the broadcaster, the Sun Device, the bomb. Their `proceed` branch rebuilds the flagship as the bomb ship, takes its landers, gives 1,000,000 RU, moves it to Earth and runs 14 days.
   - **Port and record their talk in 3b-2, and leave its entries and the bomb ship to 3c, with a question then about whose ship carries the bomb (recommended).**
   - Leave the whole script to 3c.
9. **The Thraddash's body count.** Their alliance needs 25 of their ships destroyed in battles with them.
   - **Every player's kills in a battle with the Thraddash count (recommended),** as a battle is the group's.
   - Only the host's: guests' kills wouldn't count toward the group's alliance.
10. **The order of items.** As above (recommended): the engine pieces first; the Spathi and the Slylandro at home next, since they finish 3b-1's races; then the Melnorme, as they rescue ships out of fuel (3b-1's risk) and sell the Spathi's password. The other races then come in pairs.
    - Another order, for example the Melnorme first.
