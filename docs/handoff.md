# Handoff

Where the work stands, so the next session (on any computer) can pick it up. Keep it current: see the rule in `CLAUDE.md`. This is not a history; the plans' Progress sections and git hold that.

**Last updated:** 2026-11-16, the escorts' upgrades built, review rounds 1 and 2's findings fixed; two questions for the owner.

## Where things are

- **Branch:** `claude/ac2c-coop-engine-setup-wuj7hq` on `github.com/maudrid/Sc2c`. Everything is committed and pushed.
- **Current plan:** `docs/plans/escort-upgrades.md`, the escorts' upgrades (the owner's decision of 2026-11-13, go-ahead 2026-11-16 for this item alone): escorts buy crew pods, fuel tanks, thrusters and turning jets up to host settings (`GameSettings.EscortCaps`, placeholders 8, 6, 4 and 2, the kit's counted), by the flagship's rules and prices. Built and in review; its readings of what the decision left open are in the plan, its try-out in `docs/how-to-try/escort-upgrades.md`.
- **Stage 4b is built** (`docs/plans/stage-4b.md`, `docs/how-to-try/stage-4b.md`); stage 4a's too (`docs/plans/stage-4.md`).
- **Debugging aid:** a UQM run of one recorded battle with every frame whole can be had with a small script on `tools/extract/golden_uqm.py`'s `BattleProgram` (build `battle`, `strings` and `images` with its `build`, then `run_race_battles` on the recorded input).
- **Last checks** (the escorts' upgrades, review round 1's fixes, 2026-11-16; `tests/topic-map.json` remade): all topics 2,162 passed, 0 failed, 0 skipped with the content set; the game build has only the two long-standing warnings (`game/scripts/Main.cs` CS8602 line 1125, `game/scripts/ReportView.cs` 78); `tools/test_network.py` passed at the build (78 checks; no wire or session change since); the data checks last ran at 4a item 9 (nothing in `data/` changed since).
- **Headless runs:** always give the host a `--save` file in a scratch folder. `--load` without `--save` goes on saving to the default `host-save.json`, and `--start-fleet`, `--start-allied`, `--start-units` and the like without a `--start-*` place change the default game.

## Open items

- **The escorts' upgrades: review.** Built (`docs/plans/escort-upgrades.md`, its Progress row); rounds 1 and 2 found no correctness bugs; their doc and range fixes are in; round 3 is next.

The escorts' caps are placeholders to be set by playtesting (`design.md`'s open item).

Smaller items left for later stages are notes under the items that own them in the plans (`stage-3.md` for 3b and 3c; `stage-4b.md`'s notes, Tanaka's Shofixti scout among them, for 3b).

## Owner's rules (2026-11-12)

- **A talk is the answerer's ship's.** The group as a whole starts the story; whoever answers speaks for it with their own ship (names, hold, crew, fuel), and when the replies pass on, the talk goes on with the new answerer's ship. An opening taken back takes back what the talks changed on each ship; a save during it is without them.
- **Only battle salvage is split**, between the players in the battle when it's won (online: a disconnect leaves the battle). The starbase's 2,500 RU go to the answerer; players can give units to each other.
- **A guest's talk gives to the host's fleet (2026-11-15).** A guest who talks to the commander can bring the Arilou's ships (and any talk's ships) into the host's fleet; only the fleet's worth counts as 0 for a guest.
- **No messages for the host's fleet (2026-11-15).** Item 9 built none for the pick, the shipyard's fleet actions or the roster: the host's game acts on its own world and guests have no fleet. The owner agreed; needed only if guests may act on the host's fleet or the host's game becomes a client of its own world.
- **No ranks.** Guests as "Commander" was considered and dropped: UQM's lines and voices say "Captain".

## Decisions waiting on the owner

- **Selling a new escort's kit is money without end** (found reviewing the escorts' upgrades; not changed). A new guest's escort costs nothing: `GameWorld.Join` gives a fresh kit to any player id not seen before, and a client picks its own id (`--id`). The whole kit sells back as UQM's flagship sells its starting modules, so new ids, selling the kit and giving the units on make about 6,000 resource units an id at a new game's prices, without limit (2,500 of it the thrusters, jets and fuel tank the upgrades made sellable; the lander, storage bay, gun, fuel and crew sold before). Options:
  1. *The kit's modules sell back for 0* (the kit is a gift; what's bought keeps UQM's refund). **Recommended**, as it closes the source wherever new ids come from without limiting who joins. Not possible as things stand: a ship's slots don't say which pieces were the kit's (`ShipModules` holds module numbers and the kit's crew, nothing saying where a slot's piece came from; `PlayerShip.Loadout` keeps only the kit's counts). It needs a kit mark on each slot (set by `ShipModules.FromLoadout`, cleared when sold, kept in the save: save version 13, an older save's escort marked where its kit's pieces still are), a count for the kit's landers, and a choice for the kit's fuel and crew (about 230 more, sold at the fuel and crew prices): a small item.
  2. *A cap on new escorts per game* (a setting; `Join` refuses a new id past it): simple, but bounds the source rather than closing it, and turns away real new players past the cap.
  3. *Accept it* and record it in `design.md`'s open items (friends play together; a host can give a smaller kit, `GameSettings.EscortLoadout`).
- **The commander's advice and an escort's drives (low).** `AnalyzeCondition` (`Comm/Scripts/StarbaseScript.cs`, through `PlayerTalkShip.Thrusters`/`Jets` and `AnswererTalkShip`'s) counts thrusters, jets and modules from the flagship (stage 3, decision 4, made when escorts couldn't buy thrusters or jets) but crew, fuel and landers from the talker, so an escort asking hears the thrusters' and jets' advice by the flagship's count, whatever its own ship holds. Should the advice count the talker's own ship's thrusters and jets (and modules)?

## Decided, not built

- **A shortcut for the Arilou's gift: not now (the owner, 2026-11-16).** No shortcut reaches the commander's bulletin (`--start-docked` marks every bulletin told; `--start-devices PORTAL_SPAWNER_DEVICE` sets `PORTAL_SPAWNER_ON_SHIP`, not the `PORTAL_SPAWNER` the bulletin reads); `FleetTests` checks it and the how-to-try says so.
- **Backlog:** guests' alien hulls (`design.md`, "Backlog"), not to be built now.

## Next step

Review round 3 of the escorts' upgrades (round 2's fixes' commit), fix what holds up, and repeat until a round finds nothing (`CLAUDE.md`); then report to the owner, with the two questions above. After that, with the owner's go-ahead: stage 3b's plan (ships in space and the races, `design.md`'s build order) for the owner to approve.

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
