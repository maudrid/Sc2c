# Handoff

Where the work stands, so the next session (on any computer) can pick it up. Keep it current: see the rule in `CLAUDE.md`. This is not a history; the plans' Progress sections and git hold that.

**Last updated:** 2026-11-12, after stage 4a item 9 was built and independently reviewed.

## Where things are

- **Branch:** `claude/ac2c-coop-engine-setup-wuj7hq` on `github.com/maudrid/Sc2c`. Everything is committed and pushed.
- **Current plan:** `docs/plans/stage-4.md`, stage 4a (combat), approved by the owner with all fifteen decisions accepted.
- **Stage 4a items 1 to 9 are built.** Each has a Built row in the plan's Progress section and notes under the later items for what they must honour. Items 10 to 13 are left: 10 the host's pause, 11 the wire, 12 client views, 13 docs.
- **Last checks** (on item 9's last commit): all topics 1,396 passed, 0 failed, 0 skipped with the content set; the game build has only the two long-standing warnings (`game/scripts/Main.cs` ~976, `game/scripts/ReportView.cs` 78); data checks OK; `tools/test_network.py` passed.

## Open items

Item 9's independent review (after its five review rounds) found no bug in the fight's rules but left these, none fixed yet:

1. **The starbase's 2,500 RU go to an away starter.** `StarbaseVisit.ShareTheGift` (`src/Crateris.Core/World/StarbaseVisit.cs`) always counts the starter. If the starter disconnects from the Ilwrath fight and another player wins, the away starter takes a share (host 1,250, away starter 1,250). Stage 3's rule (decision 5, `differences.md` row 37) splits it between the players in the game, and its own test leaves an away player out. Fix: count the starter only while present (a wrecked starter is present and keeps its share); give the remainder by join order; add the test.
2. **Wrong refusal reason for gifts during the fight.** `GameWorld.cs` (~1098) checks the unsettled starter before the in-battle and wreck checks, so a gift to or from the starter mid-fight is refused as `TradeRefusal.Paused` ("The game waits while the story's talk goes on"), though nothing is paused. It should be `InBattle` or `Wrecked`. `OpeningTests.cs` (~1117, ~1159) asserts `Paused` and must change with it.
3. **Decision 13 was reworded without the owner's confirmation.** Commit 5ad8d69 changed it so a starter who disconnects from the Ilwrath fight keeps the crew it left with (decision 10) instead of being put back fully as before the visit. **The owner must confirm or reverse this.** Two notes still say it's for the owner to confirm: `docs/plans/stage-4.md` item 9's note on the starter disconnecting (~line 200) and item 13's note (~line 222). Update them once the owner decides.
4. **Stale comments in `StarbaseVisit.cs`:** `Opening` (~137) still says the visit is taken back if its starter leaves (during the fight, leaving settles the starter instead); `Undone` (~203) doesn't list the Ilwrath killed with nobody present to answer.

Smaller items left by earlier work are recorded in the plan as notes under the item that owns them (items 10 to 13, 3b, 3c, 4b).

## Next step

Ask the owner first. Then, as they choose:

1. Fix the four open items above (owner's answer on item 3 first), with the review loop in `CLAUDE.md`.
2. Then item 10, the host's pause, only when the owner says to start it.

## Working agreements

- **One item per go-ahead.** "Start item N" (or "push and start item N without me") covers that one item. When it's done, report and wait; don't push further items or start the next one unasked.
- **Push only when the owner says.** Commit freely.
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
