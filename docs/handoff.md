# Handoff

Where the work stands, so the next session (on any computer) can pick it up. Keep it current: see the rule in `CLAUDE.md`. This is not a history; the plans' Progress sections and git hold that.

**Last updated:** 2026-11-13, item 12 (client views) built and reviewed in five rounds; the fifth still found a small thing (fixed), so the loop stopped there as CLAUDE.md says, and the owner is told.

## Where things are

- **Branch:** `claude/ac2c-coop-engine-setup-wuj7hq` on `github.com/maudrid/Sc2c`. Everything is committed and pushed.
- **Current plan:** `docs/plans/stage-4.md`, stage 4a (combat), approved by the owner with all fifteen decisions accepted.
- **Stage 4a items 1 to 12 are built.** Each has a Built row in the plan's Progress section and notes under the later items for what they must honour. Item 13 (docs) is left; item 12 left it a note (the differences rows to write, the how-to-try page, protocol 22).
- **Last checks:** after item 12's fifth review round's fix, all topics 1,464 passed, 0 failed, 0 skipped with the content set; the game build has only the two long-standing warnings (`game/scripts/Main.cs` ~1088, `game/scripts/ReportView.cs` 78); `tools/test_network.py` passed on item 12's first commit (a guest in a battle over the lossy relay: about 350 bytes a snapshot; `GODOT` set to the binary inside `/opt/godot/Godot_v4.7.2-stable_mono_linux_x86_64/` here); not run again after the review fixes, which touched only drawing and the camera's two added numbers. Data checks were last run on item 9's last commit.

## Open items

None open. The owner's rules below are built (`World/AnswererTalkShip.cs`) and reviewed in five rounds: the replier reset on docking, a lander's crew counted twice, replies offered for the last answerer's ship, a lander out taken back, and saves agreeing with Undo were found and fixed; the fifth found nothing.

Smaller items left by earlier work are recorded in the plan as notes under the item that owns them (items 10 to 13, 3b, 3c, 4b).

## Owner's rules (2026-11-12)

- **A talk is the answerer's ship's.** The group as a whole starts the story; whoever answers speaks for it with their own ship (names, hold, crew, fuel), and when the replies pass on, the talk goes on with the new answerer's ship. An opening taken back takes back what the talks changed on each ship; a save during it is without them.
- **Only battle salvage is split**, between the players in the battle when it's won (online: a disconnect leaves the battle). The starbase's 2,500 RU go to the answerer; players can give units to each other.
- **No ranks.** Guests as "Commander" was considered and dropped: UQM's lines and voices say "Captain".

## Next step

Item 13 (docs), only when the owner says to start it. Item 12's review: round 1 seven findings (the pause redrawing, missed frames' stars, the inner system's zoom, a bigger arena's stars, held keys into the rescue screen, two docs), round 2 three (an own camera's star totals, the explosion's flash after missed frames, docs), round 3 two (the flash's start and a whole explosion missed), round 4 two (keys held as the crew runs out, a doc), round 5 one (a one-frame difference midway through an explosion when the frame after the crew ran out was missed): all fixed and tested. The loop stopped at five rounds as CLAUDE.md says; a sixth round wasn't run.

## Decisions for the owner (item 12)

- **The smooth zoom's scaling.** UQM's default smooth zoom scales stamps trilinearly (each frame blended with the next size's); the port scales by nearest pixels for now (sizes and places are UQM's, the pixels sharper). Porting the trilinear scaler (about 300 lines of `canvas.c`) is possible if wanted.
- **The minimap** (decision 2's) is drawn over the space window's bottom right corner, the arena round the player's ship; it could go elsewhere.

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
