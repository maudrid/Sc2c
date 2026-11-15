# Handoff

Where the work stands, so the next session (on any computer) can pick it up. Keep it current: see the rule in `CLAUDE.md`. This is not a history; the plans' Progress sections and git hold that.

**Last updated:** 2026-11-15, stage 4b item 10 (client views) built; review rounds 1 to 4's findings fixed, awaiting round 5.

## Where things are

- **Branch:** `claude/ac2c-coop-engine-setup-wuj7hq` on `github.com/maudrid/Sc2c`. Everything is committed and pushed.
- **Current plan:** `docs/plans/stage-4b.md`, stage 4b (every race's ship and the host's fleet): eleven items, approved by the owner on 2026-11-13 with all six recommendations accepted. Stage 4a's plan, `docs/plans/stage-4.md`, is built; its decisions hold in 4b.
- **Stage 4b item 1 is built and reviewed** (its Progress row): every ship's content, and the traces `tests/golden/battle-races-1.json` to `-3.json` (items 2 to 4); notes for items 3 and 4 are under them in the plan.
- **Stage 4b items 2 to 4 are built and reviewed** (their Progress rows): all 24 race ships are ported, in `src/Crateris.Core/Combat/` (`RaceShips.Ported`, so `--start-battle` takes them), every trace matched (`RaceShipBattleTests`); co-op, seam and branch tests in `RaceShipCoopTests` (a partial class, by item).
- **Stage 4b item 5 (enemy fleets) is built and reviewed** (its Progress row; five rounds: the Ur-Quan, Kohr-Ah and Earthling's salvage, the client's enemy status on a ship's return, a cap of 14 ships, the joining notice, tests on the real path; round 5 found nothing): `EnemyFleet`/`EnemyFleetShip` and `GameWorld.StartBattle(id, EnemyFleet, ...)` (one ship at a time, at most 14; infinite fleets via `Battle`'s `infiniteEnemy`), salvage the first race's bounty for each ship (`BattleKit.RaceOf`), `WorldBattle.EnemyFleetLeft` for 3b, `--start-battle RACE,RACE,...`; the markers' race is noted for items 9 and 10; tests in `EnemyFleetTests` and `BattleScreenTests`.
- **Stage 4b item 6 (the host's fleet) is built and reviewed** (its Progress row; round 1: a ship of 0 crew refused on load, `--start-*` gifts lost under `--start-visit`; round 2: wording, a test that couldn't fail, the start order for minerals and units; round 3 found nothing): `HostFleet` (`GameWorld.Fleet`, build.c's functions), the talks' ships giving to the host's fleet whoever answers (`EscortsWorth` 0 for a guest), the Arilou's gift (row 43 closed), saves version 12, `--start-fleet` and `--start-allied`; tests in `FleetTests` (topic `fleet`).
- **Stage 4b item 7 (the shipyard and the roster) is built and reviewed** (its Progress row; round 1: the roster going stale when a talk changes the fleet, docs; round 2: the roster telling its ships apart; round 3 found nothing): the host's shipyard builds allied races' ships into the slot under the cursor, crews them with UQM's refunds, the last crew's refund and the buy-back, and scraps a ship left with no crew as its crew editing ends (`ShipyardMenu`'s `YardMode.Build` and fleet crew mode, `Outfitting`'s `BuildShip`, `HireShipCrew`, `DismissShipCrew`, `ScrapShip`; `HostFleet.Build`, `Scrap`, `MoveRosterCrew`; `GameWorld.ScrapUnmannedFleetShips` each frame and as a visit changes; a save leaves such a ship out); guests are refused the hangar's slots; the roster (`Systems/RosterMenu.cs`) from the host's orbit menu's Manifest (`OrbitMenu.Fleet`, `PlayerSession.Fleet`, `GameWorld.RosterFor`/`MoveRosterCrew`); recorded against UQM's own `DoModifyShips` and `DoModifyRoster` (`golden/outfit.c` extended, `outfit.json`'s `shipyard`, `ShipyardGoldenTests`); tests in `ShipyardTests` (topic `fleet`); `--script` gains `menuleft` and `menuright`.
- **Stage 4b item 8 (fleet ships in battle) is built and reviewed** (its Progress row; round 1 fixed: a story talk's replies reaching a host on the pick screen, places drifting while the battle waits for the pick, a held Enter's repeat picking the flagship, the opening visit's save and `Undo` now leaving out only the ships its talks gave (`StarbaseVisit.FleetWithout`), a waiting pick of a ship a talk took; docs): the host with fleet ships brings them into a battle (`Combat/Battle.Armada.cs`: `Armada`, race_q[0], `JoinArmada`, `Pick`, `LeaveArmada`) and chooses each on the pick screen (`Systems/PickShipMenu.cs`, DoPickBattleShip's cursor; `World/GameWorld.FleetBattles.cs`: `PickShipKey`, `WaitsForPick`, `PickScreenFor`), the flagship the last choice; the battle waits only while nobody else fights (decision 2, `differences.md` row 61); a fleet ship that dies is gone, one that retreats keeps its crew; one share per player; saves; `PlayerSession.PickScreen` for the host's own keys and `--script`'s menu keys; tests in `FleetBattleTests` (topic `fleet`). Notes for items 9, 10 and 11 are under them in the plan.
- **Stage 4b item 9 (the wire) is built and reviewed** (its Progress row; round 3: one doc's wording; round 2: the covering rule's tests with hot spots mixed, docs, item 10's notes on the erase's odd row and the limpets' growth; round 1: the marks' peak figure in the plan, only the captain's-window marks still showing sent (`SilhouetteMarks.Shown`) and the newest past the cap, a gone Shofixti's Glory Device frame kept on its `Starship`, `WriteArmada` by index): protocol 23; the host's fleet in the host's ship (`ShipSnapshot.Fleet`, `FleetSnapshot`), each marker's race (`BattleMarkerSnapshot.EnemyShip`), the host's fleet ships and pick screen in the battle (`BattleFighterSnapshot.Armada`), each ship's captain's window as its code changes it (`BattleShipStatus.CaptainSpecial`, `Marks`: the Shofixti's Glory Device, the Orz's marines, the VUX's limpets), `YardMode.Build` encoded with its race; no new message (the reading below). Tests in `FleetWireTests` (topic `fleet`), `SnapshotCodecTests`, and every race battle's frames through the codec in `RaceShipBattleTests`; `tools/test_network.py` has a fleet battle run.
- **Stage 4b item 10 (client views) is built, review rounds 1 to 4 fixed, awaiting round 5** (its Progress row; round 4: the roster's crews drawn from the live fleet the menu acts on, not overlaid on the snapshot, and the steps recorded before a fresh drawing dropped; round 3: the roster drawn from the steps `RosterMenu` records as each key is taken, not worked out from what changed; round 2: the roster's keys between frames drawn in an order UQM's could take them, two ordinal mutations caught, docs; round 1: the marks told apart by their ordinals, protocol 25; UQM's flagship names in the battle's panel, pick screen and roster; the roster's following tested and its keys between frames drawn in order; the fleet's version bumps tested; doors dropped as a screen is drawn afresh; another's change under the host's list or crew editing leaves it; test gaps closed): every race ship's status panel and captain's window (the Orz's marines, the VUX's limpets, the Shofixti's Glory Device's frame), the player's slot drawn afresh as a new ship of theirs comes in, battle markers with their race, the pick screen over the battle for the host (`Presentation/FleetScreens.cs`, `BattleScreen`), the shipyard's hangar with the host's fleet (the doors, crew counts, the list of ships to build, the support ships; protocol 24: `StarbaseShip.Fleet`), the roster in orbit (`RosterMenu` records each step as its key is taken, `RosterEvent`; `Presentation/FleetScreens.cs`' `RosterDrawing` draws them and `RosterFollower` hands them over each frame, drawing afresh from the live fleet (`IRosterFleet`) as the roster opens or the fleet's ships change; painted by `game/scripts/RosterPanel.cs`). Checked against UQM's own drawing: `tests/golden/battle-races-draw.json` (`RaceDrawTests`) and 25 new scenarios in `screens.json` (`StarbaseScreensTests`). Readings decided are in the Progress row (a guest is told in words while the host chooses; the ship spin isn't built: `differences.md` row 63). Notes for item 11 are under it.
- **Debugging aid:** a UQM run of one recorded battle with every frame whole can be had with a small script on `tools/extract/golden_uqm.py`'s `BattleProgram` (build `battle`, `strings` and `images` with its `build`, then `run_race_battles` on the recorded input); it found the Orz's turret flash firing off its own howitzer's blast.
- **Last checks:** at item 10's review round 4, all topics 2,141 passed, 0 failed, 0 skipped with the content set (round 4's probe tests: two failing on 3fdc906 as the reviewer's probes, the third's no-area case new; its four mutations caught; round 3's four roster tests each failing with round 2's follower, run on bc0d3c3 in a worktree; round 2's new tests, 3 checks, and round 1's, 14, each seen failing with its fix undone); at item 10's build, 56 mutations, 55 caught, one equivalent; `tests/topic-map.json` remade at the build (rounds 1 to 4 added no files); the game build has only the two long-standing warnings (`game/scripts/Main.cs` CS8602, now line 1125; `game/scripts/ReportView.cs` 78); `tools/test_network.py` passed (78 checks); data checks on 4a item 9's last commit (items 1 to 10 changed nothing in `data/`).

## Open items

None open. The owner's rules below are built (`World/AnswererTalkShip.cs`) and reviewed in five rounds: the replier reset on docking, a lander's crew counted twice, replies offered for the last answerer's ship, a lander out taken back, and saves agreeing with Undo were found and fixed; the fifth found nothing.

Smaller items left by earlier work are recorded in the plan as notes under the item that owns them (items 10 to 13, 3b, 3c, 4b).

## Owner's rules (2026-11-12)

- **A talk is the answerer's ship's.** The group as a whole starts the story; whoever answers speaks for it with their own ship (names, hold, crew, fuel), and when the replies pass on, the talk goes on with the new answerer's ship. An opening taken back takes back what the talks changed on each ship; a save during it is without them.
- **Only battle salvage is split**, between the players in the battle when it's won (online: a disconnect leaves the battle). The starbase's 2,500 RU go to the answerer; players can give units to each other.
- **A guest's talk gives to the host's fleet (2026-11-15).** A guest who talks to the commander can bring the Arilou's ships (and any talk's ships) into the host's fleet; only the fleet's worth counts as 0 for a guest.
- **No messages for the host's fleet (2026-11-15).** Item 9 built none for the pick, the shipyard's fleet actions or the roster: the host's game acts on its own world and guests have no fleet. The owner agreed; needed only if guests may act on the host's fleet or the host's game becomes a client of its own world.
- **No ranks.** Guests as "Commander" was considered and dropped: UQM's lines and voices say "Captain".

## Decisions waiting on the owner

- The escorts' upgrade caps (placeholders suggested: thrusters 8, turning jets 6, fuel tanks 4, crew pods 2), and whether that small item is built on its own or added to stage 4b.

## Decided, not built

- **Escorts' upgrades (the owner, 2026-11-13):** escorts may buy crew pods, fuel tanks, thrusters and turning jets, each capped below the flagship by a setting; the flagship keeps high-efficiency fuel systems and the bomb's parts (`design.md`'s open item, `differences.md` row 9). Not yet in any plan: outfitting (`Starbase/Outfitting.cs`) refuses them to escorts now. Needs the caps' numbers (placeholders until playtesting) and a go-ahead to build.
- **Backlog:** guests' alien hulls (`design.md`, "Backlog"), not to be built now.

## Next step

Stage 4b item 10 (client views): review round 5, the last the coordinator runs (`CLAUDE.md`). Then, with the owner's go-ahead, item 11 (docs).

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
