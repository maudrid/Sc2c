# Handoff

Where the work stands, so the next session (on any computer) can pick it up. Keep it current: see the rule in `CLAUDE.md`. This is not a history; the plans' Progress sections and git hold that.

**Last updated:** 2026-11-14, stage 4b item 3 (ships, part 2) built and reviewed in three rounds (round 1: a co-op gap, kept targets without crew, and four test gaps; round 2: two untested branches of that fix; round 3: nothing to fix).

## Where things are

- **Branch:** `claude/ac2c-coop-engine-setup-wuj7hq` on `github.com/maudrid/Sc2c`. Everything is committed and pushed.
- **Current plan:** `docs/plans/stage-4b.md`, stage 4b (every race's ship and the host's fleet): eleven items, approved by the owner on 2026-11-13 with all six recommendations accepted. Stage 4a's plan, `docs/plans/stage-4.md`, is built; its decisions hold in 4b.
- **Stage 4b item 1 is built and reviewed** (its Progress row): every ship's content, and the traces `tests/golden/battle-races-1.json` to `-3.json` (items 2 to 4); notes for items 3 and 4 are under them in the plan.
- **Stage 4b item 2 is built and reviewed** (its Progress row): the Arilou, Spathi, Supox, Druuge, Yehat, Zoq-Fot-Pik, Thraddash and Umgah.
- **Stage 4b item 3 is built** (its Progress row), review rounds 1 and 2's findings fixed (round 2: tests for the drop's cloak exclusion and the fighters' immediate track; round 1: fighters and blades leave a target without crew for the nearest opposing ship with crew; tests for the Mmrnmhrm's weapon after a transform, the Utwig AI's halving, the fighter's head-on bounce, north-south seam cases): the Utwig, Mycon, Androsynth, Mmrnmhrm, Chenjesu, Slylandro, Ur-Quan and Kohr-Ah in `src/Crateris.Core/Combat/` (in `RaceShips.Ported`, so `--start-battle` takes them), all 16 of their traces matched (`RaceShipBattleTests`); co-op, seam and branch tests in `RaceShipCoopTests` (a partial class, item 3's in `RaceShipCoopTests.Item3.cs`), each new one checked to fail on its mutation (the row lists them, and the few equivalent or untested).
- **Last checks:** with item 3 and review rounds 1 and 2's fixes, all topics 1,668 passed (combat 433), 0 failed, 0 skipped with the content set; `tests/topic-map.json` remade; the game build has only the two long-standing warnings (`game/scripts/Main.cs` ~1088, `game/scripts/ReportView.cs` 78); a headless `--start-battle urquan --script "none:40,fire:100,special:20,none:200" --quit-after 400` ran (in battle against the urquan, which destroyed the flagship at frame 337: game over). `tools/test_network.py` was last run on 4a item 12's first commit; data checks on 4a item 9's last commit (items 1 to 3 changed nothing in `data/`).

## In progress: stage 4b item 4

- **Built so far** (committed and pushed as each matched): the Earthling (`HumanShip.cs`), Syreen (`SyreenShip.cs`), Pkunk (`PkunkShip.cs`; its last insult per battle, `Battle.ShipState`, reset as each Pkunk is made), VUX (`VuxShip.cs`; the limpets' marks by `Battle.ModifySilhouette` into `Starship.Silhouette`, on `Starship.Icons`, which the world and the trace tests now set), Shofixti (`ShofixtiShip.cs`, the recruited one; its Glory Device counting on its captain sheet's frames; `OrzShip.cs` is only a stub for its marine test until the Orz is ported), Chmmr (`ChmmrShip.cs`) and Melnorme (`MelnormeShip.cs`; frames' sizes at every zoom now in `Sheet.Rects`, `Frame.Rect`, for GetFrameRect), in `RaceShips.Ported`; their fifteen traces in `battle-races-3.json` match, `pkunk-rebirth` among them (`RaceShipBattleTests`).
- **Left:** the Orz; the co-op, seam and branch tests (`RaceShipCoopTests.Item4.cs`); mutation probes; `Options.cs`'s usage; `differences.md`; the topic map; the Progress row.

## Open items

None open. The owner's rules below are built (`World/AnswererTalkShip.cs`) and reviewed in five rounds: the replier reset on docking, a lander's crew counted twice, replies offered for the last answerer's ship, a lander out taken back, and saves agreeing with Undo were found and fixed; the fifth found nothing.

Smaller items left by earlier work are recorded in the plan as notes under the item that owns them (items 10 to 13, 3b, 3c, 4b).

## Owner's rules (2026-11-12)

- **A talk is the answerer's ship's.** The group as a whole starts the story; whoever answers speaks for it with their own ship (names, hold, crew, fuel), and when the replies pass on, the talk goes on with the new answerer's ship. An opening taken back takes back what the talks changed on each ship; a save during it is without them.
- **Only battle salvage is split**, between the players in the battle when it's won (online: a disconnect leaves the battle). The starbase's 2,500 RU go to the answerer; players can give units to each other.
- **No ranks.** Guests as "Commander" was considered and dropped: UQM's lines and voices say "Captain".

## Decisions waiting on the owner

- The escorts' upgrade caps (placeholders suggested: thrusters 8, turning jets 6, fuel tanks 4, crew pods 2), and whether that small item is built on its own or added to stage 4b.

## Decided, not built

- **Escorts' upgrades (the owner, 2026-11-13):** escorts may buy crew pods, fuel tanks, thrusters and turning jets, each capped below the flagship by a setting; the flagship keeps high-efficiency fuel systems and the bomb's parts (`design.md`'s open item, `differences.md` row 9). Not yet in any plan: outfitting (`Starbase/Outfitting.cs`) refuses them to escorts now. Needs the caps' numbers (placeholders until playtesting) and a go-ahead to build.
- **Backlog:** guests' alien hulls (`design.md`, "Backlog"), not to be built now.

## Next step

Stage 4b item 4 (ships, part 3) is being built (the owner's go-ahead, 2026-11-14): see "In progress" below. When it's built, the next step is the review of item 4.

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
