# Handoff

Where the work stands, so the next session (on any computer) can pick it up. Keep it current: see the rule in `CLAUDE.md`. This is not a history; the plans' Progress sections and git hold that.

**Last updated:** 2026-11-18, stage 3b-1 item 3 (encounters in hyperspace) built with the owner's go-ahead for it alone (`docs/plans/stage-3b.md`, "Progress"); review round 1's findings fixed (its Progress row), round 2 not yet run. Earlier: items 1 and 2 built, reviewed and reported (2026-11-17).

## Where things are

- **Branch:** `claude/ac2c-coop-engine-setup-wuj7hq` on `github.com/maudrid/Sc2c`. Everything is committed and pushed.
- **Current plan:** `docs/plans/stage-3b.md`, stage 3b split in two (3b-1 ships in space with five races, 3b-2 the other races and the Melnorme), 3b-1 planned in detail: approved 2026-11-17 with its eleven decisions (decision 8 shared, decision 11 notices added by the owner). Item 1, the clock's events, is built and reviewed (its "Progress" row). Item 2, the fleets' data, is built and reviewed (its "Progress" row: races.h's fleet tables in `data/races.json`, each race's `FLEET_INFO` in the story state, `check_race_growth`, `SetRaceDest` and `StartSphereTracking` ported, the fleets in save version 13 as an optional part, the two weeks' and the opening's talks' take-back, `clock-events.json` recording the fleets from UQM's own `InitGameStructures`). Item 3, encounters in hyperspace, is built and review round 1's findings fixed (a ship spinning into a star stays in hyperspace for the encounters until the spin's last frame, recorded from UQM; tests for fresh numbers, wrecks and each player's fuel; notes for 3b-2's broadcaster and item 5's meeting spin; row 69), round 2 not yet run (its "Progress" row: the shared list, each player's daily roll with their own numbers, the groups flying at the nearest player, kept for players who left, saved, taken back with the two weeks; matched frame by frame with UQM's own hyperspace in `hyperspace-encounters.json`; the hyperspace flight's scroll aligned to a whole pixel as UQM's). Items 4 to 12 aren't started.
- **Last plan built:** `docs/plans/escort-upgrades.md`, the escorts' upgrades (the owner's decision of 2026-11-13, go-ahead 2026-11-16 for this item alone): escorts buy crew pods, fuel tanks, thrusters and turning jets up to host settings (`GameSettings.EscortCaps`, placeholders 8, 6, 4 and 2, the kit's counted), by the flagship's rules and prices. Built and reviewed; its readings of what the decision left open are in the plan (the owner's answers of 2026-11-17 to its two questions with them), its try-out in `docs/how-to-try/escort-upgrades.md`.
- **Stage 4b is built** (`docs/plans/stage-4b.md`, `docs/how-to-try/stage-4b.md`); stage 4a's too (`docs/plans/stage-4.md`).
- **Debugging aid:** a UQM run of one recorded battle with every frame whole can be had with a small script on `tools/extract/golden_uqm.py`'s `BattleProgram` (build `battle`, `strings` and `images` with its `build`, then `run_race_battles` on the recorded input).
- **Last checks** (3b-1 item 3's review round 1, 2026-11-18): all topics 2,205 passed, 0 failed, 0 skipped with the content set; the game build has only the two long-standing warnings (`game/scripts/Main.cs` CS8602 line 1125, `game/scripts/ReportView.cs` 78); `golden_uqm.py --only hyperspace-encounters` records the committed file (a ninth scenario, into a star); `tools/test_network.py` not run (no session or wire change; it passed for item 3); the data checks weren't run (no data change); no files added, so the topic map wasn't remade.
- **Headless runs:** always give the host a `--save` file in a scratch folder. `--load` without `--save` goes on saving to the default `host-save.json`, and `--start-fleet`, `--start-allied`, `--start-units` and the like without a `--start-*` place change the default game.

## Open items

- Item 1's left-open points (the plan's Progress row): 3c's events are set aside, not run (`differences.md` row 66, which now says a fleet's arrival event would be set aside too).
- Item 3's left-open points (its Progress row; round 1 added notes for 3b-2's broadcaster, under the 3b-2 row, and item 5's meeting spin, under item 5): a meeting ends at once until item 5 (`differences.md` row 69); no race's groups stay until items 6 to 8 (row 68, `GameWorld.EncounterRaces`); the encounters aren't sent to clients or drawn (items 10, 11). Item 3 decided the two weeks: they roll for the players in hyperspace, and an undone opening takes back what they rolled and the numbers drawn.
- Item 2's left-open points (its Progress row): the fleets' `crew_level`, `max_crew` and `max_energy` are the race's ship's, not kept per fleet; the fleets aren't sent to clients until there's a starmap; the talks' `StartSphereTracking` runs for the opening's only, the meetings' come with items 5 to 8.
- The escorts' caps are placeholders to be set by playtesting (`design.md`'s open item).

Smaller items left for later stages are notes under the items that own them in the plans (`stage-3.md` for 3b and 3c; `stage-4b.md`'s notes for 3b; Tanaka's Shofixti scout and the Melnorme's trading for escorts are in `stage-3b.md`'s 3b-2 row).

## Owner's rules (2026-11-12)

- **A talk is the answerer's ship's.** The group as a whole starts the story; whoever answers speaks for it with their own ship (names, hold, crew, fuel), and when the replies pass on, the talk goes on with the new answerer's ship. An opening taken back takes back what the talks changed on each ship; a save during it is without them.
- **Only battle salvage is split**, between the players in the battle when it's won (online: a disconnect leaves the battle). The starbase's 2,500 RU go to the answerer; players can give units to each other.
- **A guest's talk gives to the host's fleet (2026-11-15).** A guest who talks to the commander can bring the Arilou's ships (and any talk's ships) into the host's fleet; only the fleet's worth counts as 0 for a guest.
- **No messages for the host's fleet (2026-11-15).** Item 9 built none for the pick, the shipyard's fleet actions or the roster: the host's game acts on its own world and guests have no fleet. The owner agreed; needed only if guests may act on the host's fleet or the host's game becomes a client of its own world.
- **No ranks.** Guests as "Commander" was considered and dropped: UQM's lines and voices say "Captain".

## Decided, not built

- **A shortcut for the Arilou's gift: not now (the owner, 2026-11-16).** No shortcut reaches the commander's bulletin (`--start-docked` marks every bulletin told; `--start-devices PORTAL_SPAWNER_DEVICE` sets `PORTAL_SPAWNER_ON_SHIP`, not the `PORTAL_SPAWNER` the bulletin reads); `FleetTests` checks it and the how-to-try says so.
- **Backlog:** guests' alien hulls (`design.md`, "Backlog"), not to be built now.

## Next step

Review round 2 of stage 3b-1 item 3 (the coordinator's), on round 1's fixes; then report to the owner and wait for the go-ahead for item 4.

## Left for later (the owner, 2026-11-13: visual fidelity is fixed later)

- **The smooth zoom's scaling.** UQM's default smooth zoom scales stamps trilinearly (each frame blended with the next size's); the port scales by nearest pixels (sizes and places are UQM's, the pixels sharper). Porting the trilinear scaler (about 300 lines of `canvas.c`) waits for a visual-fidelity pass.
- **The minimap's place** (over the space window's bottom right corner) may move then too.

## Working agreements

- **One item per go-ahead.** "Start item N" (or "push and start item N without me") covers that one item. When it's done, report and wait; don't push further items or start the next one unasked.
- **Push freely.** The owner said (2026-11-12) to push whenever; commit and push as work goes.
- **Mind the usage budget.** Each item has cost a builder subagent, up to five review rounds and an independent review (mutation testing and probes), often an hour or more of agents. Keep rounds short when they're only finding wording, and say what's running.
- **Subagents get cut off.** Builders sometimes hand back mid-loop; their reviewer may still be running and leave a mutated file in the tree. Check `git status` and running agents before going on, and have the reviewer finish and restore rather than reverting its files yourself.

## Setting up on another computer

- **Commit identity:** this repo's commits are `Claude <noreply@anthropic.com>`, set in the repo's local git config. In a fresh clone run `git config user.name Claude` and `git config user.email noreply@anthropic.com` before committing. Don't change the global identity.
- **Commit trailer:** end commit messages with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- **Toolchain:** .NET 8 SDK and Godot 4 mono. Set `GODOT` to the mono build for `tools/test_network.py`.
- **UQM content:** never committed. The game downloads it on first start into Godot's user folder (on Linux `~/.local/share/godot/app_userdata/Attack on Crateris/content/uqm-0.8.0-content.uqm`). Point `CRATERIS_CONTENT` at it for tests and headless runs (`docs/how-to-try/content-stage.md`).
- **UQM 0.8.0 source:** expected beside the repo at `../uqm-0.8.0/sc2`, for the golden recorders and data checks (`golden_uqm.py --uqm PATH --content PACKAGE --only NAME,...`; `--only battle-races` takes about two minutes).
- **Commands:**
  - tests: `python3 tools/run_tests.py` (`--changed` while working; `--map` to remake `tests/topic-map.json`)
  - build: `dotnet build game/Crateris.Game.csproj`
  - data checks: `UQM_SC2=$(realpath ../uqm-0.8.0/sc2) python3 tools/extract/test_data.py`
  - network: `python3 tools/test_network.py`
