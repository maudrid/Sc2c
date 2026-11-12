# Handoff

Where the work stands, so the next session (on any computer) can pick it up. Keep it current: see the rule in `CLAUDE.md`. This is not a history; the plans' Progress sections and git hold that.

**Last updated:** 2026-11-12, after item 9's independent review's four open items were fixed and reviewed (three rounds, the last finding nothing).

## Where things are

- **Branch:** `claude/ac2c-coop-engine-setup-wuj7hq` on `github.com/maudrid/Sc2c`. Everything is committed and pushed.
- **Current plan:** `docs/plans/stage-4.md`, stage 4a (combat), approved by the owner with all fifteen decisions accepted.
- **Stage 4a items 1 to 9 are built.** Each has a Built row in the plan's Progress section and notes under the later items for what they must honour. Items 10 to 13 are left: 10 the host's pause, 11 the wire, 12 client views, 13 docs.
- **Last checks:** on the open items' fixes (e5d97f6), all topics 1,399 passed, 0 failed, 0 skipped with the content set. The game build, data checks and `tools/test_network.py` were last run on item 9's last commit (only the two long-standing build warnings, `game/scripts/Main.cs` ~976 and `game/scripts/ReportView.cs` 78; the rest OK); the fixes touch only Core and docs.

## Open items

Item 9's independent review left four items; the owner confirmed decision 13 as reworded (a starter disconnecting from the Ilwrath fight keeps the crew it left with), and the other three are fixed: an away starter has no share of the starbase's gift (`StarbaseVisit.ShareTheGift`, tests `AStarterAwayFromTheFightHasNoShareOfTheGift`, `AWreckedStarterKeepsItsShareOfTheGift`), gifts to or from the starter mid-fight are refused as `InBattle` (`GameWorld.Give`), a mineral sale in the gift's talk is no longer split with the gift, and `StarbaseVisit`'s stale comments are updated. Three review rounds: the first found the gift split also shared a mineral sale in the same talk (fixed: only the gift is split); the second found doc gaps and a missing test (fixed); the third found nothing.

**For the owner (not changed):** with the starter away from the Ilwrath fight, the replier in the "starbase is ready" talk is offered the starter's `have_minerals` and may sell the away starter's minerals (the units at UQM's worth go to the starter's hold). This follows stage 3's decision 7 (the talk stays the starter's ship's); the alternative is to offer the sale only to the starter itself.

Smaller items left by earlier work are recorded in the plan as notes under the item that owns them (items 10 to 13, 3b, 3c, 4b).

## Next step

Item 10, the host's pause, only when the owner says to start it.

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
