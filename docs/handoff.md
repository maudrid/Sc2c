# Handoff

Where the work stands, so the next session (on any computer) can pick it up. Keep it current: see the rule in `CLAUDE.md`. This is not a history; the plans' Progress sections and git hold that.

**Last updated:** 2026-11-16, stage 4b item 11 (docs) built, review rounds 1 and 2's findings fixed, awaiting the next review round.

## Where things are

- **Branch:** `claude/ac2c-coop-engine-setup-wuj7hq` on `github.com/maudrid/Sc2c`. Everything is committed and pushed.
- **Current plan:** `docs/plans/stage-4b.md`, stage 4b (every race's ship and the host's fleet), approved by the owner on 2026-11-13. Items 1 to 10 are built and reviewed; what each built and what its reviews found is in its Progress row. Stage 4a's plan, `docs/plans/stage-4.md`, is built; its decisions hold in 4b.
- **Item 11 (docs) is built and in review** (its Progress row; review rounds 1 and 2's findings fixed; `tests/topic-map.json` remade): `docs/how-to-try/stage-4b.md` (every command in its shortcuts list run headless as written, its output quoted; four screenshots taken under Xvfb, `docs/screenshots/stage4b-*.png`), `differences.md` row 65 and rows 9, 13, 43 and 44, `design.md`, `porting-inventory.md`, `uqm-data.md`, `how-to-try/stage-3a.md`, `Options.cs`'s usage; `FleetWireTests.FiftyLimpetsOnOneShipFitASnapshotsBudget` measures item 9's watch note (50 limpets on one ship: 324 bytes).
- **Debugging aid:** a UQM run of one recorded battle with every frame whole can be had with a small script on `tools/extract/golden_uqm.py`'s `BattleProgram` (build `battle`, `strings` and `images` with its `build`, then `run_race_battles` on the recorded input).
- **Last checks** (item 11, review round 2): all topics 2,144 passed, 0 failed, 0 skipped with the content set; the game build has only the two long-standing warnings (`game/scripts/Main.cs` CS8602 line 1125, `game/scripts/ReportView.cs` 78); `tools/test_network.py` passed at item 11's build (78 checks); the data checks last ran at 4a item 9 (nothing in `data/` changed since).
- **Headless runs:** always give the host a `--save` file in a scratch folder. `--load` without `--save` goes on saving to the default `host-save.json`, and `--start-fleet`, `--start-allied`, `--start-units` and the like without a `--start-*` place change the default game.

## Open items

- **The status panel's first line** still says "Attack on Crateris - stage 3a": a code string, the first of the status lines (`src/Crateris.Core/Session/PlayerSession.cs`, line 1160), shown on every screen. Found at item 11 and not changed, since item 11 is docs only; waiting on the owner ("Decisions waiting on the owner").

Smaller items left for later stages are notes under the items that own them in the plans (`stage-3.md` for 3b and 3c; `stage-4b.md`'s notes, Tanaka's Shofixti scout among them, for 3b).

## Owner's rules (2026-11-12)

- **A talk is the answerer's ship's.** The group as a whole starts the story; whoever answers speaks for it with their own ship (names, hold, crew, fuel), and when the replies pass on, the talk goes on with the new answerer's ship. An opening taken back takes back what the talks changed on each ship; a save during it is without them.
- **Only battle salvage is split**, between the players in the battle when it's won (online: a disconnect leaves the battle). The starbase's 2,500 RU go to the answerer; players can give units to each other.
- **A guest's talk gives to the host's fleet (2026-11-15).** A guest who talks to the commander can bring the Arilou's ships (and any talk's ships) into the host's fleet; only the fleet's worth counts as 0 for a guest.
- **No messages for the host's fleet (2026-11-15).** Item 9 built none for the pick, the shipyard's fleet actions or the roster: the host's game acts on its own world and guests have no fleet. The owner agreed; needed only if guests may act on the host's fleet or the host's game becomes a client of its own world.
- **No ranks.** Guests as "Commander" was considered and dropped: UQM's lines and voices say "Captain".

## Decisions waiting on the owner

- The escorts' upgrade caps (placeholders suggested: thrusters 8, turning jets 6, fuel tanks 4, crew pods 2), and when that small item is built (on its own, or in a later stage's plan; stage 4b is built without it).
- **The status panel's "stage 3a" title (found at item 11):** fix it with item 11 (a one-line change and its tests), or later.
- **A shortcut for the Arilou's gift (found at item 11).** The plan promised it could be tried with `--start-devices` at the starbase, but no shortcut reaches it: `--start-docked` marks every bulletin told, and `--start-devices PORTAL_SPAWNER_DEVICE` sets `PORTAL_SPAWNER_ON_SHIP`, not the `PORTAL_SPAWNER` field the bulletin reads. `FleetTests` checks it; the how-to-try says so. A shortcut (a `--start-visit` step, say) needs the owner's go-ahead.

## Decided, not built

- **Escorts' upgrades (the owner, 2026-11-13):** escorts may buy crew pods, fuel tanks, thrusters and turning jets, each capped below the flagship by a setting; the flagship keeps high-efficiency fuel systems and the bomb's parts (`design.md`'s open item, `differences.md` row 9). Not yet in any plan: outfitting (`Starbase/Outfitting.cs`) refuses them to escorts now. Needs the caps' numbers (placeholders until playtesting) and a go-ahead to build.
- **Backlog:** guests' alien hulls (`design.md`, "Backlog"), not to be built now.

## Next step

Review item 11's fixes (the review loop in `CLAUDE.md`) until a round finds nothing. Then report item 11 to the owner with the two questions found in it (the status panel's "stage 3a" title, fixed with item 11 or later; a shortcut for the Arilou's gift), and stage 4b is done; the owner decides what's next.

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
