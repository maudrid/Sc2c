# Handoff

Where the work stands, so the next session (on any computer) can pick it up. Keep it current: see the rule in `CLAUDE.md`. This is not a history; the plans' Progress sections and git hold that.

**Last updated:** 2026-11-13, stage 4b's plan drafted (`docs/plans/stage-4b.md`); waiting on the owner's six decisions.

## Where things are

- **Branch:** `claude/ac2c-coop-engine-setup-wuj7hq` on `github.com/maudrid/Sc2c`. Everything is committed and pushed.
- **Current plan:** `docs/plans/stage-4b.md`, stage 4b (every race's ship and the host's fleet), a draft: eleven items, six decisions for the owner. Stage 4a's plan, `docs/plans/stage-4.md`, is built; its decisions hold in 4b.
- **Stage 4a is built: items 1 to 13.** Each has a Built row in the plan's Progress section. Item 13 wrote `docs/how-to-try/stage-4a.md`, `differences.md` rows (new rows 48 to 58), and brought `design.md`, `porting-inventory.md`, `uqm-data.md`, `how-to-try/stage-3a.md` and `Options.cs`'s usage up to date.
- **Last checks:** with item 13's docs, all topics 1,464 passed, 0 failed, 0 skipped with the content set; the game build has only the two long-standing warnings (`game/scripts/Main.cs` ~1088, `game/scripts/ReportView.cs` 78); three headless runs for the how-to-try page (the Ilwrath fight from `--start-visit base-report`, a retreat from `--start-battle`, the host's pause) did what the page says. `tools/test_network.py` was last run on item 12's first commit; data checks on item 9's last commit.

## Open items

None open. The owner's rules below are built (`World/AnswererTalkShip.cs`) and reviewed in five rounds: the replier reset on docking, a lander's crew counted twice, replies offered for the last answerer's ship, a lander out taken back, and saves agreeing with Undo were found and fixed; the fifth found nothing.

Smaller items left by earlier work are recorded in the plan as notes under the item that owns them (items 10 to 13, 3b, 3c, 4b).

## Owner's rules (2026-11-12)

- **A talk is the answerer's ship's.** The group as a whole starts the story; whoever answers speaks for it with their own ship (names, hold, crew, fuel), and when the replies pass on, the talk goes on with the new answerer's ship. An opening taken back takes back what the talks changed on each ship; a save during it is without them.
- **Only battle salvage is split**, between the players in the battle when it's won (online: a disconnect leaves the battle). The starbase's 2,500 RU go to the answerer; players can give units to each other.
- **No ranks.** Guests as "Commander" was considered and dropped: UQM's lines and voices say "Captain".

## Decisions waiting on the owner

Stage 4b's plan, `docs/plans/stage-4b.md` "Decisions": (1) who flies a fleet ship (recommended: the host, as UQM); (2) whether the battle stops while the host picks the next ship (recommended: only when no other player ship is fighting); (3) who has a fleet (recommended: only the host, who alone builds, crews and scraps); (4) a fleet ship that dies (recommended: gone, as UQM); (5) the salvage with fleet ships (recommended: one share per player); (6) ship code that picks "the" opponent (recommended: the nearest; effects in range hit every player ship in range; teammates spared).

## Decided, not built

- **Escorts' upgrades (the owner, 2026-11-13):** escorts may buy crew pods, fuel tanks, thrusters and turning jets, each capped below the flagship by a setting; the flagship keeps high-efficiency fuel systems and the bomb's parts (`design.md`'s open item, `differences.md` row 9). Not yet in any plan: outfitting (`Starbase/Outfitting.cs`) refuses them to escorts now. Needs the caps' numbers (placeholders until playtesting) and a go-ahead to build.
- **Backlog:** guests' alien hulls (`design.md`, "Backlog"), not to be built now.

## Next step

The owner answers stage 4b's decisions (or approves the recommendations); then item 1 on their go-ahead.

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
- **UQM 0.8.0 source:** expected beside the repo at `../uqm-0.8.0/sc2`, for the golden recorders and data checks.
- **Commands:**
  - tests: `python3 tools/run_tests.py` (`--changed` while working; `--map` to remake `tests/topic-map.json`)
  - build: `dotnet build game/Crateris.Game.csproj`
  - data checks: `UQM_SC2=$(realpath ../uqm-0.8.0/sc2) python3 tools/extract/test_data.py`
  - network: `python3 tools/test_network.py`
